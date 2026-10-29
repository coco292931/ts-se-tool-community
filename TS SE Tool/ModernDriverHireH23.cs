using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

using TS_SE_Tool.Utilities;

namespace TS_SE_Tool
{
    public partial class FormMain
    {
        // RC5.1H23 - contratar motorista IA em save moderno (formato 75+).
        //
        // Modelo descoberto nos saves reais do ETS2 1.61 (formato 102):
        //  - a garagem tem arrays de tamanho fixo igual a capacidade, com "null" nos lugares vazios;
        //  - capacidade vem do campo status: 0 = nao comprada, 6 = 1, 2 = 3, 3 = 5;
        //  - contratar = ocupar um lugar "null" com o driver e preencher hometown/current_city.
        //
        // Limite conhecido: nao mexemos em economy.drivers_offer nem em economy.driver_pool.
        // O jogo faz "troca de lugar" entre esses dois arrays ao contratar, e a escrita em
        // texto puro nao aceita remocao de linha (o carregador responde
        // "There are multiple unit trees in the file"). O motorista contratado pode
        // continuar aparecendo na lista de recrutamento ate o jogo reorganizar o mercado.

        internal sealed class ModernHireGarageH23
        {
            public string Id;
            public string City;
            public int Capacity;
            public int UsedDrivers;
            public int FreeSlots;
            public override string ToString() { return City + " (" + UsedDrivers + "/" + Capacity + ")"; }
        }

        internal sealed class ModernHireCandidateH23
        {
            public string Id;
            public int Experience;
            public string Skills;
            public override string ToString() { return Id + "  XP " + Experience.ToString(CultureInfo.InvariantCulture) + "  " + Skills; }
        }

        private const int ModernHireFeeH23 = 1500;

        private ToolStripMenuItem ModernDriverHireMenuH23;

        private static int GetModernGarageCapacityH23(int status)
        {
            if (status == 6) return 1;
            if (status == 2) return 3;
            if (status == 3) return 5;
            return 0;
        }

        private static int GetModernBlockEndH23(string[] text, int header)
        {
            for (int index = header + 1; index < text.Length; index++)
            {
                if (text[index] == "}")
                    return index;
            }

            return -1;
        }

        private static int GetModernScalarH23(string[] text, int start, int end, string tag, out string value)
        {
            value = null;

            for (int index = start; index < end; index++)
            {
                if (text[index].StartsWith(" " + tag + ": ", StringComparison.Ordinal))
                {
                    value = text[index].Substring(tag.Length + 3);
                    if (value != null)
                        value = value.Trim();
                    return index;
                }
            }

            return -1;
        }

        internal bool CanUseModernDriverHireH23(out string reason)
        {
            reason = "";

            if (!IsModernCompatibilityMode())
            {
                reason = "Disponivel apenas em saves modernos (formato acima de 74).";
                return false;
            }

            if (tempSavefileInMemory == null || tempSavefileInMemory.Length == 0)
            {
                reason = "Nenhum save moderno carregado.";
                return false;
            }

            return true;
        }

        internal List<ModernHireGarageH23> GetModernHireGaragesH23()
        {
            List<ModernHireGarageH23> result = new List<ModernHireGarageH23>();
            string[] text = tempSavefileInMemory;
            if (text == null)
                return result;

            for (int index = 0; index < text.Length; index++)
            {
                if (!text[index].StartsWith("garage : ", StringComparison.Ordinal))
                    continue;

                int end = GetModernBlockEndH23(text, index);
                if (end < 0)
                    continue;

                string statusText;
                if (GetModernScalarH23(text, index + 1, end, "status", out statusText) < 0)
                    continue;

                int status;
                if (!Int32.TryParse(statusText, NumberStyles.Integer, CultureInfo.InvariantCulture, out status))
                    continue;

                int capacity = GetModernGarageCapacityH23(status);
                if (capacity <= 0)
                    continue;

                int used = 0;
                int free = 0;

                for (int line = index + 1; line < end; line++)
                {
                    if (!text[line].StartsWith(" drivers[", StringComparison.Ordinal))
                        continue;

                    int colon = text[line].IndexOf(": ", StringComparison.Ordinal);
                    if (colon < 0)
                        continue;

                    string value = text[line].Substring(colon + 2).Trim();

                    if (value == "null")
                        free++;
                    else
                        used++;
                }

                if (free <= 0)
                    continue;

                string id = text[index].Substring("garage : ".Length).TrimEnd(' ', '{').Trim();

                result.Add(new ModernHireGarageH23
                {
                    Id = id,
                    City = id.StartsWith("garage.", StringComparison.Ordinal) ? id.Substring("garage.".Length) : id,
                    Capacity = capacity,
                    UsedDrivers = used,
                    FreeSlots = free
                });
            }

            return result;
        }

        internal List<ModernHireCandidateH23> GetModernHireCandidatesH23()
        {
            List<ModernHireCandidateH23> result = new List<ModernHireCandidateH23>();
            string[] text = tempSavefileInMemory;
            if (text == null)
                return result;

            HashSet<string> hired = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> offered = new HashSet<string>(StringComparer.Ordinal);

            for (int index = 0; index < text.Length; index++)
            {
                if (text[index].StartsWith("garage : ", StringComparison.Ordinal))
                {
                    int end = GetModernBlockEndH23(text, index);
                    if (end < 0)
                        continue;

                    for (int line = index + 1; line < end; line++)
                    {
                        if (!text[line].StartsWith(" drivers[", StringComparison.Ordinal))
                            continue;

                        int colon = text[line].IndexOf(": ", StringComparison.Ordinal);
                        if (colon < 0)
                            continue;

                        string value = text[line].Substring(colon + 2).Trim();
                        if (value.StartsWith("driver.", StringComparison.Ordinal))
                            hired.Add(value);
                    }
                }
                else if (text[index].StartsWith(" drivers_offer[", StringComparison.Ordinal))
                {
                    int colon = text[index].IndexOf(": ", StringComparison.Ordinal);
                    if (colon < 0)
                        continue;

                    string value = text[index].Substring(colon + 2).Trim();
                    if (value.StartsWith("driver.", StringComparison.Ordinal))
                        offered.Add(value);
                }
            }

            foreach (string driverId in offered)
            {
                if (hired.Contains(driverId))
                    continue;

                int header = -1;
                for (int index = 0; index < text.Length; index++)
                {
                    if (text[index] == "driver_ai : " + driverId + " {")
                    {
                        header = index;
                        break;
                    }
                }

                if (header < 0)
                    continue;

                int end = GetModernBlockEndH23(text, header);
                if (end < 0)
                    continue;

                string home;
                if (GetModernScalarH23(text, header + 1, end, "hometown", out home) < 0)
                    continue;

                if (home != "\"\"")
                    continue;

                string experienceText;
                int experience = 0;
                if (GetModernScalarH23(text, header + 1, end, "experience_points", out experienceText) >= 0)
                    Int32.TryParse(experienceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out experience);

                string skills = "";
                foreach (string tag in new string[] { "adr", "long_dist", "heavy", "fragile", "urgent", "mechanical" })
                {
                    string skillText;
                    if (GetModernScalarH23(text, header + 1, end, tag, out skillText) >= 0)
                        skills += tag.Substring(0, Math.Min(3, tag.Length)) + skillText + " ";
                }

                result.Add(new ModernHireCandidateH23
                {
                    Id = driverId,
                    Experience = experience,
                    Skills = skills.Trim()
                });
            }

            return result;
        }

        internal bool HireModernDriverH23(string driverId, string garageId, out string message)
        {
            string reason;
            if (!CanUseModernDriverHireH23(out reason))
            {
                message = reason;
                return false;
            }

            message = "";

            List<ModernHireGarageH23> garages = GetModernHireGaragesH23();
            ModernHireGarageH23 garage = garages.FirstOrDefault(x => x.Id == garageId);
            if (garage == null)
            {
                message = "Garagem sem lugar livre ou inexistente: " + garageId;
                return false;
            }

            List<ModernHireCandidateH23> candidates = GetModernHireCandidatesH23();
            if (!candidates.Any(x => x.Id == driverId))
            {
                message = "Motorista indisponivel para contratacao: " + driverId;
                return false;
            }

            string[] text = tempSavefileInMemory;

            // 1) ocupar o lugar vazio da garagem
            int garageHeader = -1;
            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] == "garage : " + garageId + " {")
                {
                    garageHeader = index;
                    break;
                }
            }

            if (garageHeader < 0)
            {
                message = "Bloco da garagem nao localizado: " + garageId;
                return false;
            }

            int garageEnd = GetModernBlockEndH23(text, garageHeader);
            if (garageEnd < 0)
            {
                message = "Bloco da garagem incompleto: " + garageId;
                return false;
            }

            int slotLine = -1;
            for (int line = garageHeader + 1; line < garageEnd; line++)
            {
                if (!text[line].StartsWith(" drivers[", StringComparison.Ordinal))
                    continue;

                int colon = text[line].IndexOf(": ", StringComparison.Ordinal);
                if (colon < 0)
                    continue;

                if (text[line].Substring(colon + 2).Trim() != "null")
                    continue;

                slotLine = line;
                break;
            }

            if (slotLine < 0)
            {
                message = "A garagem nao tem lugar de motorista vazio: " + garageId;
                return false;
            }

            // 2) bloco do motorista: hometown / current_city / training_policy
            int driverHeader = -1;
            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] == "driver_ai : " + driverId + " {")
                {
                    driverHeader = index;
                    break;
                }
            }

            if (driverHeader < 0)
            {
                message = "Bloco do motorista nao localizado: " + driverId;
                return false;
            }

            int driverEnd = GetModernBlockEndH23(text, driverHeader);
            if (driverEnd < 0)
            {
                message = "Bloco do motorista incompleto: " + driverId;
                return false;
            }

            string city = garage.City;
            int changed = 0;

            for (int line = driverHeader + 1; line < driverEnd; line++)
            {
                if (text[line] == " hometown: \"\"")
                {
                    text[line] = " hometown: " + city;
                    changed++;
                }
                else if (text[line] == " current_city: \"\"")
                {
                    text[line] = " current_city: " + city;
                    changed++;
                }
                else if (text[line].StartsWith(" training_policy: ", StringComparison.Ordinal))
                {
                    text[line] = " training_policy: 1";
                    changed++;
                }
            }

            if (changed < 2)
            {
                message = "Campos do motorista nao localizados: " + driverId;
                return false;
            }

            // 3) taxa de contratacao
            int bankHeader = -1;
            for (int index = 0; index < text.Length; index++)
            {
                if (text[index].StartsWith("bank : ", StringComparison.Ordinal))
                {
                    bankHeader = index;
                    break;
                }
            }

            if (bankHeader >= 0)
            {
                int bankEnd = GetModernBlockEndH23(text, bankHeader);
                if (bankEnd > bankHeader)
                {
                    for (int line = bankHeader + 1; line < bankEnd; line++)
                    {
                        if (!text[line].StartsWith(" money_account: ", StringComparison.Ordinal))
                            continue;

                        long current;
                        if (!Int64.TryParse(text[line].Substring(" money_account: ".Length), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out current))
                            break;

                        long after = current - ModernHireFeeH23;
                        if (after < 0)
                            after = 0;

                        text[line] = " money_account: " + after.ToString(CultureInfo.InvariantCulture);

                        if (SiiNunitData != null && SiiNunitData.Bank != null &&
                            SiiNunitData.Bank.money_account > (uint)ModernHireFeeH23)
                        {
                            SiiNunitData.Bank.money_account -= (uint)ModernHireFeeH23;
                        }

                        break;
                    }
                }
            }

            // 4) ocupar o lugar por ultimo, depois de todas as validacoes
            int colon2 = text[slotLine].IndexOf(": ", StringComparison.Ordinal);
            text[slotLine] = text[slotLine].Substring(0, colon2 + 2) + driverId;

            message = "Contratado " + driverId + " em " + garage.City + "." + Environment.NewLine +
                      "Sera gravado ao salvar o save." + Environment.NewLine + Environment.NewLine +
                      "Observacao: o motorista pode continuar listado no recrutamento ate o jogo" + Environment.NewLine +
                      "reorganizar o mercado.";

            IO_Utilities.LogWriter(
                "RC5.1H23 driver hired: " + driverId + " -> " + garageId +
                ", fee=" + ModernHireFeeH23.ToString(CultureInfo.InvariantCulture) +
                ", free_before=" + garage.FreeSlots.ToString(CultureInfo.InvariantCulture));

            return true;
        }

        private void EnsureModernDriverHireMenuH23()
        {
            if (ModernDriverHireMenuH23 != null)
                return;

            ModernDriverHireMenuH23 = new ToolStripMenuItem();
            ModernDriverHireMenuH23.Name = "toolStripMenuItemDriverHireH23";
            ModernDriverHireMenuH23.Text = "Contratar motorista IA (RC5.1H23)";
            ModernDriverHireMenuH23.ToolTipText =
                "Ocupa um lugar vazio da garagem em save moderno.";
            ModernDriverHireMenuH23.Click += delegate { ShowModernDriverHireDialogH23(); };

            int separatorIndex = toolStripMenuItemProgram.DropDownItems.IndexOf(toolStripSeparator1);
            if (separatorIndex < 0)
                toolStripMenuItemProgram.DropDownItems.Add(ModernDriverHireMenuH23);
            else
                toolStripMenuItemProgram.DropDownItems.Insert(separatorIndex, ModernDriverHireMenuH23);
        }

        private void ShowModernDriverHireDialogH23()
        {
            string reason;
            if (!CanUseModernDriverHireH23(out reason))
            {
                MessageBox.Show(reason, "RC5.1H23", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<ModernHireGarageH23> garages = GetModernHireGaragesH23();
            List<ModernHireCandidateH23> candidates = GetModernHireCandidatesH23();

            if (garages.Count == 0)
            {
                MessageBox.Show(
                    "Nenhuma garagem com lugar de motorista vazio." + Environment.NewLine +
                    "Compre ou amplie uma garagem no jogo antes de contratar.",
                    "RC5.1H23", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (candidates.Count == 0)
            {
                MessageBox.Show(
                    "Nenhum motorista disponivel no recrutamento deste save.",
                    "RC5.1H23", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (Form dialog = new Form())
            {
                dialog.Text = "RC5.1H23 - contratar motorista";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ClientSize = new System.Drawing.Size(520, 240);

                Label garageLabel = new Label();
                garageLabel.Text = "Garagem:";
                garageLabel.Location = new System.Drawing.Point(16, 16);
                garageLabel.AutoSize = true;

                ComboBox garageBox = new ComboBox();
                garageBox.DropDownStyle = ComboBoxStyle.DropDownList;
                garageBox.Location = new System.Drawing.Point(16, 38);
                garageBox.Size = new System.Drawing.Size(488, 24);
                garageBox.DataSource = garages;

                Label driverLabel = new Label();
                driverLabel.Text = "Motorista no recrutamento:";
                driverLabel.Location = new System.Drawing.Point(16, 74);
                driverLabel.AutoSize = true;

                ComboBox driverBox = new ComboBox();
                driverBox.DropDownStyle = ComboBoxStyle.DropDownList;
                driverBox.Location = new System.Drawing.Point(16, 96);
                driverBox.Size = new System.Drawing.Size(488, 24);
                driverBox.DataSource = candidates;

                Label feeLabel = new Label();
                feeLabel.Text = "Custo: " +
                    ModernHireFeeH23.ToString(CultureInfo.InvariantCulture) + " (descontado do banco)";
                feeLabel.Location = new System.Drawing.Point(16, 132);
                feeLabel.AutoSize = true;

                Button okButton = new Button();
                okButton.Text = "Contratar";
                okButton.Location = new System.Drawing.Point(316, 180);
                okButton.Size = new System.Drawing.Size(90, 30);
                okButton.DialogResult = DialogResult.OK;

                Button cancelButton = new Button();
                cancelButton.Text = "Cancelar";
                cancelButton.Location = new System.Drawing.Point(414, 180);
                cancelButton.Size = new System.Drawing.Size(90, 30);
                cancelButton.DialogResult = DialogResult.Cancel;

                dialog.Controls.Add(garageLabel);
                dialog.Controls.Add(garageBox);
                dialog.Controls.Add(driverLabel);
                dialog.Controls.Add(driverBox);
                dialog.Controls.Add(feeLabel);
                dialog.Controls.Add(okButton);
                dialog.Controls.Add(cancelButton);
                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                ModernHireGarageH23 selectedGarage = garageBox.SelectedItem as ModernHireGarageH23;
                ModernHireCandidateH23 selectedDriver = driverBox.SelectedItem as ModernHireCandidateH23;

                if (selectedGarage == null || selectedDriver == null)
                    return;

                string message;
                if (!HireModernDriverH23(selectedDriver.Id, selectedGarage.Id, out message))
                {
                    MessageBox.Show(message, "RC5.1H23", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                MessageBox.Show(message, "RC5.1H23", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void InitializeModernDriverHireH23()
        {
            EnsureModernDriverHireMenuH23();
        }
    }
}
