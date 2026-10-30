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
        // RC5.1H23 - hire and fire AI drivers on modern saves (format above 74).
        //
        // Everything below was derived from real ETS2 1.61 / save format 102 saves,
        // not guessed:
        //
        //   garage.status 6 / 2 / 3  = capacity 1 / 3 / 5
        //   garage driver arrays are sized to the capacity, empty slots hold "null"
        //   driver.state low bits are the work state, bit 0x80000000 marks "dismissed"
        //
        //   hiring = take a driver out of economy.drivers_offer, occupy a free garage
        //            slot with it, set hometown / current_city / training_policy and
        //            register it in player.drivers + driver_flags +
        //            driver_readiness_timer
        //   firing = free the garage slot, set bit 0x80000000 on driver.state,
        //            unregister it from player.drivers + driver_flags +
        //            driver_readiness_timer and append it to player.dismissed_drivers
        //
        // A driver may be owned by at most one of these three containers:
        // player.drivers, economy.drivers_offer, economy.driver_pool.
        // Garage slots do not claim ownership. Leaving a driver owned twice - or by
        // nobody after being pulled out of a container - makes the game loader fail
        // with the misleading message "There are multiple unit trees in the file".
        //
        // Money is deliberately not touched. This is a save editor; reproducing the
        // in-game hiring fee would be pointless.

        internal sealed class ModernHireGarageH23
        {
            public string Id;
            public string City;
            public int Capacity;
            public int UsedDrivers;
            public int FreeSlots;
            public override string ToString() { return City + " (" + UsedDrivers + "/" + Capacity + ")"; }
        }

        internal sealed class ModernDriverChoiceH23
        {
            public string Id;
            public string Where;
            public int Experience;
            public string Skills;
            public override string ToString()
            {
                return Id + "  " + Where + "  XP " +
                    Experience.ToString(CultureInfo.InvariantCulture) + "  " + Skills;
            }
        }

        private ToolStripMenuItem ModernDriverHireMenuH23;
        private ToolStripMenuItem ModernDriverFireMenuH23;

        private static int GetModernGarageCapacityH23(int status)
        {
            if (status == 6) return 1;
            if (status == 2) return 3;
            if (status == 3) return 5;
            return 0;
        }

        private static int FindModernUnitHeaderH23(List<string> lines, string prefix)
        {
            for (int index = 0; index < lines.Count; index++)
            {
                string line = lines[index];
                if (!line.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                if (line.Length > prefix.Length && line[prefix.Length] == ' ')
                    return index;
            }
            return -1;
        }

        private static int GetModernBlockEndH23(List<string> lines, int header)
        {
            for (int index = header + 1; index < lines.Count; index++)
                if (lines[index] == "}")
                    return index;
            return -1;
        }

        private static string GetModernFieldH23(List<string> lines, int start, int end, string tag)
        {
            string key = " " + tag + ": ";
            for (int index = start; index < end; index++)
                if (lines[index].StartsWith(key, StringComparison.Ordinal))
                    return lines[index].Substring(key.Length).Trim();
            return null;
        }

        private static bool SetModernFieldH23(List<string> lines, int start, int end, string tag, string value)
        {
            string key = " " + tag + ": ";
            for (int index = start; index < end; index++)
            {
                if (!lines[index].StartsWith(key, StringComparison.Ordinal))
                    continue;
                lines[index] = key + value;
                return true;
            }
            return false;
        }

        private static int GetModernArrayCountH23(List<string> lines, string unitPrefix, string tag,
            out int countIdx, out List<int> itemIdx, out int declared)
        {
            countIdx = -1;
            itemIdx = new List<int>();
            declared = 0;

            int header = FindModernUnitHeaderH23(lines, unitPrefix);
            if (header < 0)
                return -1;

            int end = GetModernBlockEndH23(lines, header);
            if (end < 0)
                return -1;

            string countKey = " " + tag + ": ";
            string itemPrefix = " " + tag + "[";

            for (int index = header + 1; index < end; index++)
            {
                if (lines[index].StartsWith(countKey, StringComparison.Ordinal))
                {
                    countIdx = index;
                    Int32.TryParse(lines[index].Substring(countKey.Length).Trim(),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out declared);
                }
                else if (lines[index].StartsWith(itemPrefix, StringComparison.Ordinal))
                {
                    itemIdx.Add(index);
                }
            }

            return (countIdx < 0) ? -1 : header;
        }

        private static bool AppendModernArrayItemH23(List<string> lines, string unitPrefix, string tag, string value)
        {
            int countIdx;
            List<int> itemIdx;
            int declared;

            if (GetModernArrayCountH23(lines, unitPrefix, tag, out countIdx, out itemIdx, out declared) < 0)
                return false;

            if (itemIdx.Count != declared)
                return false;

            int insertAt = (itemIdx.Count > 0) ? itemIdx[itemIdx.Count - 1] + 1 : countIdx + 1;

            lines.Insert(insertAt, " " + tag + "[" + declared.ToString(CultureInfo.InvariantCulture) + "]: " + value);
            lines[countIdx] = " " + tag + ": " + (declared + 1).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private static bool RemoveModernArrayItemAtH23(List<string> lines, string unitPrefix, string tag, int position)
        {
            int countIdx;
            List<int> itemIdx;
            int declared;

            if (GetModernArrayCountH23(lines, unitPrefix, tag, out countIdx, out itemIdx, out declared) < 0)
                return false;

            if (itemIdx.Count != declared)
                return false;

            if (position < 0 || position >= itemIdx.Count)
                return false;

            lines.RemoveAt(itemIdx[position]);

            string itemPrefix = " " + tag + "[";

            for (int k = position + 1; k < itemIdx.Count; k++)
            {
                int at = itemIdx[k] - 1;
                if (at < 0 || at >= lines.Count)
                    return false;

                int colon = lines[at].IndexOf(": ", StringComparison.Ordinal);
                if (colon < 0)
                    return false;

                lines[at] = itemPrefix + (k - 1).ToString(CultureInfo.InvariantCulture) + "]: " +
                    lines[at].Substring(colon + 2).Trim();
            }

            lines[countIdx] = " " + tag + ": " + (declared - 1).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private static int IndexOfModernArrayItemH23(List<string> lines, string unitPrefix, string tag, string value)
        {
            int countIdx;
            List<int> itemIdx;
            int declared;

            if (GetModernArrayCountH23(lines, unitPrefix, tag, out countIdx, out itemIdx, out declared) < 0)
                return -1;

            for (int k = 0; k < itemIdx.Count; k++)
            {
                int colon = lines[itemIdx[k]].IndexOf(": ", StringComparison.Ordinal);
                if (colon < 0)
                    continue;
                if (lines[itemIdx[k]].Substring(colon + 2).Trim() == value)
                    return k;
            }

            return -1;
        }

        internal bool CanUseModernDriverToolsH23(out string reason)
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

            List<string> lines = new List<string>(text);

            for (int index = 0; index < lines.Count; index++)
            {
                if (!lines[index].StartsWith("garage : ", StringComparison.Ordinal))
                    continue;

                int end = GetModernBlockEndH23(lines, index);
                if (end < 0)
                    continue;

                string statusText = GetModernFieldH23(lines, index + 1, end, "status");
                if (statusText == null)
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
                    if (!lines[line].StartsWith(" drivers[", StringComparison.Ordinal))
                        continue;

                    int colon = lines[line].IndexOf(": ", StringComparison.Ordinal);
                    if (colon < 0)
                        continue;

                    if (lines[line].Substring(colon + 2).Trim() == "null")
                        free++;
                    else
                        used++;
                }

                if (free <= 0)
                    continue;

                string id = lines[index].Substring("garage : ".Length).TrimEnd(' ', '{').Trim();

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

        private static string DescribeModernDriverH23(List<string> lines, string driverId)
        {
            int header = FindModernUnitHeaderH23(lines, "driver_ai : " + driverId);
            if (header < 0)
                return "";

            int end = GetModernBlockEndH23(lines, header);
            if (end < 0)
                return "";

            int experience = 0;
            string experienceText = GetModernFieldH23(lines, header + 1, end, "experience_points");
            if (experienceText != null)
                Int32.TryParse(experienceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out experience);

            string skills = "";
            foreach (string tag in new string[] { "adr", "long_dist", "heavy", "fragile", "urgent", "mechanical" })
            {
                string value = GetModernFieldH23(lines, header + 1, end, tag);
                if (value != null)
                    skills += tag.Substring(0, Math.Min(3, tag.Length)) + value + " ";
            }

            return experience.ToString(CultureInfo.InvariantCulture) + "|" + skills.Trim();
        }

        internal List<ModernDriverChoiceH23> GetModernHireCandidatesH23()
        {
            List<ModernDriverChoiceH23> result = new List<ModernDriverChoiceH23>();
            if (tempSavefileInMemory == null)
                return result;

            List<string> lines = new List<string>(tempSavefileInMemory);

            HashSet<string> taken = new HashSet<string>(StringComparer.Ordinal);

            for (int index = 0; index < lines.Count; index++)
            {
                if (lines[index].StartsWith("garage : ", StringComparison.Ordinal))
                {
                    int end = GetModernBlockEndH23(lines, index);
                    if (end < 0)
                        continue;

                    for (int line = index + 1; line < end; line++)
                    {
                        if (!lines[line].StartsWith(" drivers[", StringComparison.Ordinal))
                            continue;

                        int colon = lines[line].IndexOf(": ", StringComparison.Ordinal);
                        if (colon < 0)
                            continue;

                        string value = lines[line].Substring(colon + 2).Trim();
                        if (value.StartsWith("driver.", StringComparison.Ordinal))
                            taken.Add(value);
                    }
                }
                else if (lines[index].StartsWith(" drivers_offer[", StringComparison.Ordinal))
                {
                    int colon = lines[index].IndexOf(": ", StringComparison.Ordinal);
                    if (colon < 0)
                        continue;

                    string id = lines[index].Substring(colon + 2).Trim();
                    if (!id.StartsWith("driver.", StringComparison.Ordinal))
                        continue;

                    if (taken.Contains(id))
                        continue;

                    string info = DescribeModernDriverH23(lines, id);
                    if (info.Length == 0)
                        continue;

                    string[] parts = info.Split(new char[] { '|' }, 2);

                    int experience = 0;
                    Int32.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out experience);

                    result.Add(new ModernDriverChoiceH23
                    {
                        Id = id,
                        Where = "recrutamento",
                        Experience = experience,
                        Skills = parts.Length > 1 ? parts[1] : ""
                    });
                }
            }

            return result;
        }

        internal List<ModernDriverChoiceH23> GetModernFireCandidatesH23()
        {
            List<ModernDriverChoiceH23> result = new List<ModernDriverChoiceH23>();
            if (tempSavefileInMemory == null)
                return result;

            List<string> lines = new List<string>(tempSavefileInMemory);

            // the player himself is a "driver_player" unit - never offer it for firing
            string playerDriverId = "";
            int playerDriverHeader = FindModernUnitHeaderH23(lines, "driver_player");
            if (playerDriverHeader >= 0)
            {
                string[] tokens = lines[playerDriverHeader].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 3)
                    playerDriverId = tokens[2];
            }

            Dictionary<string, string> placed = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int index = 0; index < lines.Count; index++)
            {
                if (!lines[index].StartsWith("garage : ", StringComparison.Ordinal))
                    continue;

                int end = GetModernBlockEndH23(lines, index);
                if (end < 0)
                    continue;

                string garageId = lines[index].Substring("garage : ".Length).TrimEnd(' ', '{').Trim();
                string city = garageId.StartsWith("garage.", StringComparison.Ordinal)
                    ? garageId.Substring("garage.".Length) : garageId;

                for (int line = index + 1; line < end; line++)
                {
                    if (!lines[line].StartsWith(" drivers[", StringComparison.Ordinal))
                        continue;

                    int colon = lines[line].IndexOf(": ", StringComparison.Ordinal);
                    if (colon < 0)
                        continue;

                    string id = lines[line].Substring(colon + 2).Trim();
                    if (!id.StartsWith("driver.", StringComparison.Ordinal))
                        continue;
                    if (id == playerDriverId)
                        continue;

                    placed[id] = city;
                }
            }

            foreach (KeyValuePair<string, string> pair in placed)
            {
                string info = DescribeModernDriverH23(lines, pair.Key);
                string[] parts = info.Length == 0 ? new string[] { "0", "" } : info.Split(new char[] { '|' }, 2);

                int experience = 0;
                Int32.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out experience);

                result.Add(new ModernDriverChoiceH23
                {
                    Id = pair.Key,
                    Where = pair.Value,
                    Experience = experience,
                    Skills = parts.Length > 1 ? parts[1] : ""
                });
            }

            result.Sort(delegate(ModernDriverChoiceH23 a, ModernDriverChoiceH23 b)
            {
                return String.CompareOrdinal(a.Id, b.Id);
            });

            return result;
        }

        internal bool HireModernDriverH23(string driverId, string garageId, out string message)
        {
            string reason;
            if (!CanUseModernDriverToolsH23(out reason))
            {
                message = reason;
                return false;
            }

            message = "";

            ModernHireGarageH23 garage = GetModernHireGaragesH23().FirstOrDefault(x => x.Id == garageId);
            if (garage == null)
            {
                message = "Garagem sem lugar livre ou inexistente: " + garageId;
                return false;
            }

            if (!GetModernHireCandidatesH23().Any(x => x.Id == driverId))
            {
                message = "Motorista indisponivel para contratacao: " + driverId;
                return false;
            }

            List<string> lines = new List<string>(tempSavefileInMemory);

            // 1) occupy a free garage slot
            int garageHeader = FindModernUnitHeaderH23(lines, "garage : " + garageId);
            if (garageHeader < 0)
            {
                message = "Bloco da garagem nao localizado: " + garageId;
                return false;
            }

            int garageEnd = GetModernBlockEndH23(lines, garageHeader);
            if (garageEnd < 0)
            {
                message = "Bloco da garagem incompleto: " + garageId;
                return false;
            }

            int slotLine = -1;
            for (int line = garageHeader + 1; line < garageEnd; line++)
            {
                if (!lines[line].StartsWith(" drivers[", StringComparison.Ordinal))
                    continue;

                int colon = lines[line].IndexOf(": ", StringComparison.Ordinal);
                if (colon < 0)
                    continue;

                if (lines[line].Substring(colon + 2).Trim() != "null")
                    continue;

                slotLine = line;
                break;
            }

            if (slotLine < 0)
            {
                message = "A garagem nao tem lugar de motorista vazio: " + garageId;
                return false;
            }

            int slotColon = lines[slotLine].IndexOf(": ", StringComparison.Ordinal);
            lines[slotLine] = lines[slotLine].Substring(0, slotColon + 2) + driverId;

            // 2) driver block
            int driverHeader = FindModernUnitHeaderH23(lines, "driver_ai : " + driverId);
            if (driverHeader < 0)
            {
                message = "Bloco do motorista nao localizado: " + driverId;
                return false;
            }

            int driverEnd = GetModernBlockEndH23(lines, driverHeader);
            if (driverEnd < 0)
            {
                message = "Bloco do motorista incompleto: " + driverId;
                return false;
            }

            SetModernFieldH23(lines, driverHeader + 1, driverEnd, "hometown", garage.City);
            SetModernFieldH23(lines, driverHeader + 1, driverEnd, "current_city", garage.City);
            SetModernFieldH23(lines, driverHeader + 1, driverEnd, "training_policy", "1");

            // 3) register in the company roster
            if (!AppendModernArrayItemH23(lines, "player", "drivers", driverId) ||
                !AppendModernArrayItemH23(lines, "player", "driver_flags", "0") ||
                !AppendModernArrayItemH23(lines, "player", "driver_readiness_timer", "0"))
            {
                message = "Nao foi possivel registrar o motorista na empresa (bloco player).";
                return false;
            }

            // 4) pull him out of the recruitment list - do this last,
            //    it is the only step that removes a line
            int offerIndex = IndexOfModernArrayItemH23(lines, "economy", "drivers_offer", driverId);
            if (offerIndex >= 0)
            {
                if (!RemoveModernArrayItemAtH23(lines, "economy", "drivers_offer", offerIndex))
                {
                    message = "Nao foi possivel remover o motorista do recrutamento.";
                    return false;
                }
            }

            tempSavefileInMemory = lines.ToArray();

            message = "Contratado " + driverId + " em " + garage.City + "." + Environment.NewLine +
                      "Gravado ao salvar o save.";

            IO_Utilities.LogWriter(
                "RC5.1H23 driver hired: " + driverId + " -> " + garageId +
                ", free_before=" + garage.FreeSlots.ToString(CultureInfo.InvariantCulture));

            return true;
        }

        internal bool FireModernDriverH23(string driverId, out string message)
        {
            string reason;
            if (!CanUseModernDriverToolsH23(out reason))
            {
                message = reason;
                return false;
            }

            message = "";

            if (!GetModernFireCandidatesH23().Any(x => x.Id == driverId))
            {
                message = "Motorista nao esta empregado: " + driverId;
                return false;
            }

            List<string> lines = new List<string>(tempSavefileInMemory);

            // 1) release the garage slot
            int slotLine = -1;
            for (int index = 0; index < lines.Count && slotLine < 0; index++)
            {
                if (!lines[index].StartsWith("garage : ", StringComparison.Ordinal))
                    continue;

                int end = GetModernBlockEndH23(lines, index);
                if (end < 0)
                    continue;

                for (int line = index + 1; line < end; line++)
                {
                    if (!lines[line].StartsWith(" drivers[", StringComparison.Ordinal))
                        continue;

                    int colon = lines[line].IndexOf(": ", StringComparison.Ordinal);
                    if (colon < 0)
                        continue;

                    if (lines[line].Substring(colon + 2).Trim() != driverId)
                        continue;

                    lines[line] = lines[line].Substring(0, colon + 2) + "null";
                    slotLine = line;
                    break;
                }
            }

            if (slotLine < 0)
            {
                message = "O motorista nao ocupa lugar em nenhuma garagem: " + driverId;
                return false;
            }

            // 2) mark the driver as dismissed
            int driverHeader = FindModernUnitHeaderH23(lines, "driver_ai : " + driverId);
            if (driverHeader < 0)
            {
                message = "Bloco do motorista nao localizado: " + driverId;
                return false;
            }

            int driverEnd = GetModernBlockEndH23(lines, driverHeader);
            if (driverEnd < 0)
            {
                message = "Bloco do motorista incompleto: " + driverId;
                return false;
            }

            string stateText = GetModernFieldH23(lines, driverHeader + 1, driverEnd, "state");
            uint state;
            if (stateText == null ||
                !UInt32.TryParse(stateText, NumberStyles.Integer, CultureInfo.InvariantCulture, out state))
            {
                message = "Campo state do motorista nao localizado: " + driverId;
                return false;
            }

            SetModernFieldH23(lines, driverHeader + 1, driverEnd, "state",
                (state | 0x80000000u).ToString(CultureInfo.InvariantCulture));

            // 3) unregister from the company roster (same ordinal in all three arrays)
            int rosterIndex = IndexOfModernArrayItemH23(lines, "player", "drivers", driverId);
            if (rosterIndex >= 0)
            {
                if (!RemoveModernArrayItemAtH23(lines, "player", "drivers", rosterIndex))
                {
                    message = "Nao foi possivel remover o motorista do quadro da empresa.";
                    return false;
                }

                RemoveModernArrayItemAtH23(lines, "player", "driver_flags", rosterIndex);
                RemoveModernArrayItemAtH23(lines, "player", "driver_readiness_timer", rosterIndex);
            }

            // 4) remember the dismissal
            AppendModernArrayItemH23(lines, "player", "dismissed_drivers", driverId);

            tempSavefileInMemory = lines.ToArray();

            message = "Demitido " + driverId + "." + Environment.NewLine + "Gravado ao salvar o save.";

            IO_Utilities.LogWriter("RC5.1H23 driver fired: " + driverId);

            return true;
        }

        private void EnsureModernDriverMenusH23()
        {
            if (ModernDriverHireMenuH23 == null)
            {
                ModernDriverHireMenuH23 = new ToolStripMenuItem();
                ModernDriverHireMenuH23.Name = "toolStripMenuItemDriverHireH23";
                ModernDriverHireMenuH23.Text = "Contratar motorista IA (RC5.1H23)";
                ModernDriverHireMenuH23.ToolTipText =
                    "Ocupa um lugar vazio da garagem e registra o motorista na empresa.";
                ModernDriverHireMenuH23.Click += delegate { ShowModernDriverDialogH23(true); };
            }

            if (ModernDriverFireMenuH23 == null)
            {
                ModernDriverFireMenuH23 = new ToolStripMenuItem();
                ModernDriverFireMenuH23.Name = "toolStripMenuItemDriverFireH23";
                ModernDriverFireMenuH23.Text = "Demitir motorista IA (RC5.1H23)";
                ModernDriverFireMenuH23.ToolTipText =
                    "Libera o lugar na garagem e marca o motorista como demitido.";
                ModernDriverFireMenuH23.Click += delegate { ShowModernDriverDialogH23(false); };
            }

            int separatorIndex = toolStripMenuItemProgram.DropDownItems.IndexOf(toolStripSeparator1);

            foreach (ToolStripMenuItem item in new ToolStripMenuItem[] { ModernDriverHireMenuH23, ModernDriverFireMenuH23 })
            {
                if (toolStripMenuItemProgram.DropDownItems.Contains(item))
                    continue;

                if (separatorIndex < 0)
                    toolStripMenuItemProgram.DropDownItems.Add(item);
                else
                    toolStripMenuItemProgram.DropDownItems.Insert(separatorIndex, item);
            }
        }

        private void ShowModernDriverDialogH23(bool hire)
        {
            string reason;
            if (!CanUseModernDriverToolsH23(out reason))
            {
                MessageBox.Show(reason, "RC5.1H23", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<ModernHireGarageH23> garages = hire ? GetModernHireGaragesH23() : new List<ModernHireGarageH23>();
            List<ModernDriverChoiceH23> drivers = hire ? GetModernHireCandidatesH23() : GetModernFireCandidatesH23();

            if (hire && garages.Count == 0)
            {
                MessageBox.Show(
                    "Nenhuma garagem com lugar de motorista vazio." + Environment.NewLine +
                    "Compre ou amplie uma garagem no jogo antes de contratar.",
                    "RC5.1H23", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (drivers.Count == 0)
            {
                MessageBox.Show(
                    hire ? "Nenhum motorista disponivel no recrutamento deste save."
                         : "Nenhum motorista empregado ocupa lugar em garagem.",
                    "RC5.1H23", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (Form dialog = new Form())
            {
                dialog.Text = hire ? "RC5.1H23 - contratar motorista" : "RC5.1H23 - demitir motorista";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ClientSize = new System.Drawing.Size(560, 210);

                int top = 16;

                if (hire)
                {
                    Label garageLabel = new Label();
                    garageLabel.Text = "Garagem:";
                    garageLabel.Location = new System.Drawing.Point(16, top);
                    garageLabel.AutoSize = true;

                    ComboBox garageBox = new ComboBox();
                    garageBox.Name = "comboBoxModernHireGarageH23";
                    garageBox.DropDownStyle = ComboBoxStyle.DropDownList;
                    garageBox.Location = new System.Drawing.Point(16, top + 22);
                    garageBox.Size = new System.Drawing.Size(528, 24);
                    garageBox.DataSource = garages;

                    dialog.Controls.Add(garageLabel);
                    dialog.Controls.Add(garageBox);

                    top += 66;
                }

                Label driverLabel = new Label();
                driverLabel.Text = hire ? "Motorista no recrutamento:" : "Motorista empregado:";
                driverLabel.Location = new System.Drawing.Point(16, top);
                driverLabel.AutoSize = true;

                ComboBox driverBox = new ComboBox();
                driverBox.Name = "comboBoxModernDriverChoiceH23";
                driverBox.DropDownStyle = ComboBoxStyle.DropDownList;
                driverBox.Location = new System.Drawing.Point(16, top + 22);
                driverBox.Size = new System.Drawing.Size(528, 24);
                driverBox.DataSource = drivers;

                Label noteLabel = new Label();
                noteLabel.Text = "Sem custo - o save e alterado diretamente.";
                noteLabel.Location = new System.Drawing.Point(16, top + 56);
                noteLabel.AutoSize = true;

                Button okButton = new Button();
                okButton.Text = hire ? "Contratar" : "Demitir";
                okButton.Location = new System.Drawing.Point(354, 160);
                okButton.Size = new System.Drawing.Size(90, 30);
                okButton.DialogResult = DialogResult.OK;

                Button cancelButton = new Button();
                cancelButton.Text = "Cancelar";
                cancelButton.Location = new System.Drawing.Point(454, 160);
                cancelButton.Size = new System.Drawing.Size(90, 30);
                cancelButton.DialogResult = DialogResult.Cancel;

                dialog.Controls.Add(driverLabel);
                dialog.Controls.Add(driverBox);
                dialog.Controls.Add(noteLabel);
                dialog.Controls.Add(okButton);
                dialog.Controls.Add(cancelButton);
                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                ModernDriverChoiceH23 selectedDriver =
                    (dialog.Controls["comboBoxModernDriverChoiceH23"] as ComboBox).SelectedItem as ModernDriverChoiceH23;

                if (selectedDriver == null)
                    return;

                string message;

                if (hire)
                {
                    ModernHireGarageH23 selectedGarage =
                        (dialog.Controls["comboBoxModernHireGarageH23"] as ComboBox).SelectedItem as ModernHireGarageH23;

                    if (selectedGarage == null)
                        return;

                    if (!HireModernDriverH23(selectedDriver.Id, selectedGarage.Id, out message))
                    {
                        MessageBox.Show(message, "RC5.1H23", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    if (MessageBox.Show(
                            "Demitir " + selectedDriver.Id + " ?" + Environment.NewLine + Environment.NewLine +
                            "O lugar na garagem fica livre e o motorista sai do quadro da empresa.",
                            "RC5.1H23", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;

                    if (!FireModernDriverH23(selectedDriver.Id, out message))
                    {
                        MessageBox.Show(message, "RC5.1H23", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                MessageBox.Show(message, "RC5.1H23", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void InitializeModernDriverHireH23()
        {
            EnsureModernDriverMenusH23();
        }
    }
}
