using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

using TS_SE_Tool.Save.DataFormat;
using TS_SE_Tool.Utilities;

namespace TS_SE_Tool
{
    public partial class FormMain
    {
        private GroupBox ModernConvoyToolsGroupH8;
        private Label ModernConvoyStatusLabelH8;
        private Button ModernCopyTruckPositionButtonH8;
        private Button ModernPasteTruckPositionButtonH8;
        private Button ModernCopyGpsPathButtonH8;
        private Button ModernPasteGpsPathButtonH8;
        private bool ModernConvoyUiRefreshH82;
        private bool ModernConvoyEventsAttachedH82;

        private TabPage FindModernConvoyTabH8()
        {
            if (tabControlMain == null || tabControlMain.TabPages.Count == 0)
                return null;

            foreach (TabPage tabPage in tabControlMain.TabPages)
            {
                if (String.Equals(
                        tabPage.Name,
                        "tabPageConvoyTools",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return tabPage;
                }
            }

            return tabControlMain.TabPages[tabControlMain.TabPages.Count - 1];
        }

        private string EncodeModernConvoyPayloadH8(string value)
        {
            return BitConverter.ToString(
                    ZipDataUtilities.zipText(value ?? ""))
                .Replace("-", "");
        }

        private bool TryDecodeModernConvoyPayloadH8(
            out string decoded,
            out string reason)
        {
            decoded = "";
            reason = "";

            string clipboardText = Clipboard.GetText();
            if (String.IsNullOrWhiteSpace(clipboardText))
            {
                reason = "A área de transferência está vazia.";
                return false;
            }

            try
            {
                decoded = ZipDataUtilities.unzipText(clipboardText.Trim());
            }
            catch (Exception ex)
            {
                reason =
                    "O conteúdo da área de transferência não é um pacote válido do TS SE Tool. " +
                    ex.Message;
                return false;
            }

            if (String.IsNullOrWhiteSpace(decoded))
            {
                reason = "O pacote decodificado está vazio.";
                return false;
            }

            return true;
        }

        private bool CanApplyModernConvoyMutationH8(out string reason)
        {
            reason = "";

            if (!IsModernCompatibilityMode())
            {
                reason = "A função H8 é exclusiva do modo de compatibilidade moderno.";
                return false;
            }

            if (SiiNunitData == null || SiiNunitData.Player == null)
            {
                reason = "Os dados do jogador não estão carregados.";
                return false;
            }

            if (!String.IsNullOrWhiteSpace(
                    NormalizeModernReference(
                        SiiNunitData.Player.current_job)))
            {
                reason = "Finalize ou cancele a entrega ativa antes de importar posição ou rota GPS.";
                return false;
            }

            if (!String.IsNullOrWhiteSpace(
                    ModernPendingTruckSwitchTargetTruckId))
            {
                reason = "Salve ou descarte a troca de caminhão pendente antes de usar a importação H8.";
                return false;
            }

            if (HasUnsavedModernFleetChangesH6())
            {
                reason = "Salve ou descarte as alterações de manutenção da frota antes de usar a importação H8.";
                return false;
            }

            return true;
        }

        private void SetModernConvoyStatusH8(string text)
        {
            if (ModernConvoyStatusLabelH8 != null)
                ModernConvoyStatusLabelH8.Text = text ?? "";
        }

        private void ModernCopyTruckPositionH8(object sender, EventArgs e)
        {
            if (SiiNunitData == null || SiiNunitData.Player == null)
            {
                MessageBox.Show(
                    "Os dados do jogador não estão carregados.",
                    "TS SE Tool H8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string payload =
                "GPS_TruckPosition\r\n" +
                SiiNunitData.Player.my_truck_placement;

            Clipboard.SetText(EncodeModernConvoyPayloadH8(payload));
            SetModernConvoyStatusH8(
                "Posição atual copiada para a área de transferência.");

            MessageBox.Show(
                "A posição atual do caminhão foi copiada.",
                "TS SE Tool H8",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ModernPasteTruckPositionH8(object sender, EventArgs e)
        {
            string reason;
            if (!CanApplyModernConvoyMutationH8(out reason))
            {
                MessageBox.Show(
                    reason,
                    "TS SE Tool H8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string decoded;
            if (!TryDecodeModernConvoyPayloadH8(out decoded, out reason))
            {
                MessageBox.Show(
                    reason,
                    "TS SE Tool H8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string normalized = decoded
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");
            string[] lines = normalized.Split('\n');

            if (lines.Length < 2 ||
                !String.Equals(
                    lines[0].Trim(),
                    "GPS_TruckPosition",
                    StringComparison.Ordinal))
            {
                MessageBox.Show(
                    "O pacote não contém uma posição de caminhão válida.",
                    "TS SE Tool H8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string placementText = "";
            for (int index = 1; index < lines.Length; index++)
            {
                if (!String.IsNullOrWhiteSpace(lines[index]))
                {
                    if (!String.IsNullOrWhiteSpace(placementText))
                    {
                        MessageBox.Show(
                            "O pacote de posição contém dados adicionais inesperados.",
                            "TS SE Tool H8",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    placementText = lines[index].Trim();
                }
            }

            if (String.IsNullOrWhiteSpace(placementText))
            {
                MessageBox.Show(
                    "O pacote de posição não contém coordenadas.",
                    "TS SE Tool H8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                "Importar esta posição para o save carregado?\r\n\r\n" +
                "A alteração será preparada em memória e somente será gravada quando você clicar em Salvar.",
                "Confirmar posição H8",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (confirmation != DialogResult.Yes)
                return;

            // Reutiliza o importador original já compilado e homologado pelo projeto.
            // Isso evita duplicar a dependência concreta do tipo de posição no módulo H8.
            buttonGPSCurrentPositionPaste_Click(this, EventArgs.Empty);

            string importedPlacement = Convert.ToString(
                SiiNunitData.Player.my_truck_placement);

            if (!String.Equals(
                    (importedPlacement ?? "").Trim(),
                    placementText.Trim(),
                    StringComparison.Ordinal))
            {
                SetModernConvoyStatusH8(
                    "A posição não foi importada. O save permaneceu sem alteração.");
                MessageBox.Show(
                    "O importador original não confirmou as coordenadas. Nenhuma posição foi preparada.",
                    "TS SE Tool H8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SetModernConvoyStatusH8(
                "Posição importada em memória. Clique em Salvar para gravar.");

            MessageBox.Show(
                "Posição preparada. Clique em Salvar para gravar no TESTE_TSSET.",
                "TS SE Tool H8",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void AppendModernGpsSectionH8(
            StringBuilder builder,
            string sectionName,
            Dictionary<string, List<string>> source)
        {
            if (builder == null || source == null || source.Count == 0)
                return;

            builder.AppendLine(sectionName);

            foreach (KeyValuePair<string, List<string>> waypoint in source)
            {
                builder.AppendLine("waypoint");

                if (waypoint.Value == null)
                    continue;

                foreach (string line in waypoint.Value)
                    builder.AppendLine(line ?? "");
            }
        }

        private void ModernCopyGpsPathH8(object sender, EventArgs e)
        {
            StringBuilder payload = new StringBuilder();
            payload.AppendLine("GPS_Path");
            AppendModernGpsSectionH8(payload, "GPSbehind", GPSbehind);
            AppendModernGpsSectionH8(payload, "GPSahead", GPSahead);
            AppendModernGpsSectionH8(payload, "GPSavoid", GPSAvoid);

            Clipboard.SetText(EncodeModernConvoyPayloadH8(payload.ToString()));
            SetModernConvoyStatusH8(
                "Rota GPS armazenada copiada para a área de transferência.");

            MessageBox.Show(
                "A rota GPS armazenada foi copiada.",
                "TS SE Tool H8",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void AddModernParsedWaypointH8(
            string section,
            List<string> waypoint,
            List<List<string>> behind,
            List<List<string>> ahead,
            List<List<string>> avoid)
        {
            if (String.Equals(section, "GPSbehind", StringComparison.Ordinal))
                behind.Add(waypoint);
            else if (String.Equals(section, "GPSahead", StringComparison.Ordinal))
                ahead.Add(waypoint);
            else if (String.Equals(section, "GPSavoid", StringComparison.Ordinal))
                avoid.Add(waypoint);
            else
                throw new InvalidOperationException(
                    "Um waypoint foi encontrado antes da identificação da seção GPS.");
        }

        private void ReplaceModernGpsSectionH8(
            Dictionary<string, List<string>> target,
            List<List<string>> source)
        {
            if (target == null || source == null || source.Count == 0)
                return;

            target.Clear();

            foreach (List<string> waypoint in source)
            {
                target.Add(
                    GetSpareNameless(),
                    new List<string>(waypoint));
            }
        }

        private void ModernPasteGpsPathH8(object sender, EventArgs e)
        {
            string reason;
            if (!CanApplyModernConvoyMutationH8(out reason))
            {
                MessageBox.Show(
                    reason,
                    "TS SE Tool H8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string decoded;
            if (!TryDecodeModernConvoyPayloadH8(out decoded, out reason))
            {
                MessageBox.Show(
                    reason,
                    "TS SE Tool H8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string normalized = decoded
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");
            string[] lines = normalized.Split('\n');

            if (lines.Length == 0 ||
                !String.Equals(
                    lines[0].Trim(),
                    "GPS_Path",
                    StringComparison.Ordinal))
            {
                MessageBox.Show(
                    "O pacote não contém uma rota GPS válida.",
                    "TS SE Tool H8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            List<List<string>> behind = new List<List<string>>();
            List<List<string>> ahead = new List<List<string>>();
            List<List<string>> avoid = new List<List<string>>();
            string section = "";
            List<string> currentWaypoint = null;

            try
            {
                for (int index = 1; index < lines.Length; index++)
                {
                    string line = lines[index].Trim();
                    if (String.IsNullOrWhiteSpace(line))
                        continue;

                    if (String.Equals(line, "GPSbehind", StringComparison.Ordinal) ||
                        String.Equals(line, "GPSahead", StringComparison.Ordinal) ||
                        String.Equals(line, "GPSavoid", StringComparison.Ordinal))
                    {
                        section = line;
                        currentWaypoint = null;
                        continue;
                    }

                    if (String.Equals(line, "waypoint", StringComparison.Ordinal))
                    {
                        currentWaypoint = new List<string>();
                        AddModernParsedWaypointH8(
                            section,
                            currentWaypoint,
                            behind,
                            ahead,
                            avoid);
                        continue;
                    }

                    if (currentWaypoint == null)
                    {
                        throw new InvalidOperationException(
                            "Dado de rota encontrado fora de um waypoint.");
                    }

                    currentWaypoint.Add(line);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "A rota GPS não pôde ser interpretada. " + ex.Message,
                    "TS SE Tool H8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            int totalWaypoints = behind.Count + ahead.Count + avoid.Count;
            if (totalWaypoints == 0)
            {
                MessageBox.Show(
                    "O pacote GPS não contém nenhum waypoint.",
                    "TS SE Tool H8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if ((avoid.Count > 0 && GPSAvoid == null) ||
                (behind.Count > 0 && GPSbehind == null) ||
                (ahead.Count > 0 && GPSahead == null))
            {
                MessageBox.Show(
                    "O save carregado não possui a estrutura GPS necessária para esta importação.",
                    "TS SE Tool H8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                "Importar a rota GPS com " + totalWaypoints + " waypoint(s)?\r\n\r\n" +
                "A alteração será preparada em memória e somente será gravada quando você clicar em Salvar.",
                "Confirmar rota GPS H8",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (confirmation != DialogResult.Yes)
                return;

            ReplaceModernGpsSectionH8(GPSbehind, behind);
            ReplaceModernGpsSectionH8(GPSahead, ahead);
            ReplaceModernGpsSectionH8(GPSAvoid, avoid);

            SetModernConvoyStatusH8(
                "Rota GPS importada em memória. Clique em Salvar para gravar.");

            MessageBox.Show(
                "Rota GPS preparada. Clique em Salvar para gravar no TESTE_TSSET.",
                "TS SE Tool H8",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void SetModernConvoyControlTreeEnabledH82(
            Control root)
        {
            if (root == null)
                return;

            root.Enabled = true;

            foreach (Control child in root.Controls)
                SetModernConvoyControlTreeEnabledH82(child);
        }

        private void EnsureModernConvoyControlsEnabledH82()
        {
            if (!IsModernCompatibilityMode() ||
                ModernConvoyUiRefreshH82)
                return;

            ModernConvoyUiRefreshH82 = true;
            try
            {
                TabPage convoyTab = FindModernConvoyTabH8();
                if (convoyTab == null)
                    return;

                convoyTab.Enabled = true;

                if (ModernConvoyToolsGroupH8 == null ||
                    ModernConvoyToolsGroupH8.IsDisposed)
                {
                    Control[] existing = convoyTab.Controls.Find(
                        "groupBoxModernConvoyToolsH8",
                        true);

                    if (existing.Length > 0)
                        ModernConvoyToolsGroupH8 =
                            existing[0] as GroupBox;
                }

                if (ModernConvoyToolsGroupH8 != null)
                {
                    SetModernConvoyControlTreeEnabledH82(
                        ModernConvoyToolsGroupH8);
                    ModernConvoyToolsGroupH8.BringToFront();
                }
            }
            finally
            {
                ModernConvoyUiRefreshH82 = false;
            }
        }

        private void ModernConvoyUiActivationH82(
            object sender,
            EventArgs e)
        {
            if (!IsModernCompatibilityMode())
                return;

            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(
                        (MethodInvoker)delegate
                        {
                            EnsureModernConvoyControlsEnabledH82();
                        });
                }
                else
                {
                    EnsureModernConvoyControlsEnabledH82();
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
                EnsureModernConvoyControlsEnabledH82();
            }
        }

        private void AttachModernConvoyUiEventsH82(
            TabPage convoyTab)
        {
            if (convoyTab == null ||
                ModernConvoyEventsAttachedH82)
                return;

            convoyTab.Enter += ModernConvoyUiActivationH82;
            convoyTab.EnabledChanged += ModernConvoyUiActivationH82;
            tabControlMain.SelectedIndexChanged +=
                ModernConvoyUiActivationH82;

            ModernConvoyEventsAttachedH82 = true;
        }

        private Button CreateModernConvoyButtonH8(
            string name,
            string text,
            EventHandler clickHandler)
        {
            Button button = new Button();
            button.Name = name;
            button.Text = text;
            button.AutoSize = false;
            button.Width = 185;
            button.Height = 32;
            button.Margin = new Padding(6);
            button.Enabled = true;
            button.Click += clickHandler;
            return button;
        }

        private void CreateModernConvoyControlsH8()
        {
            TabPage convoyTab = FindModernConvoyTabH8();
            if (convoyTab == null)
                return;

            Control[] existing = convoyTab.Controls.Find(
                "groupBoxModernConvoyToolsH8",
                true);
            if (existing.Length > 0)
            {
                ModernConvoyToolsGroupH8 = existing[0] as GroupBox;
                AttachModernConvoyUiEventsH82(convoyTab);
                EnsureModernConvoyControlsEnabledH82();
                return;
            }

            convoyTab.Enabled = true;

            ModernConvoyToolsGroupH8 = new GroupBox();
            ModernConvoyToolsGroupH8.Name =
                "groupBoxModernConvoyToolsH8";
            ModernConvoyToolsGroupH8.Text =
                "Posição e rota GPS modernas (H8)";
            ModernConvoyToolsGroupH8.Dock = DockStyle.Bottom;
            ModernConvoyToolsGroupH8.Height = 132;
            ModernConvoyToolsGroupH8.Enabled = true;

            FlowLayoutPanel buttonsPanel = new FlowLayoutPanel();
            buttonsPanel.Name = "flowLayoutPanelModernConvoyButtonsH8";
            buttonsPanel.Dock = DockStyle.Top;
            buttonsPanel.Height = 50;
            buttonsPanel.AutoScroll = true;
            buttonsPanel.WrapContents = false;

            ModernCopyTruckPositionButtonH8 =
                CreateModernConvoyButtonH8(
                    "buttonModernCopyTruckPositionH8",
                    "Copiar posição atual",
                    ModernCopyTruckPositionH8);
            ModernPasteTruckPositionButtonH8 =
                CreateModernConvoyButtonH8(
                    "buttonModernPasteTruckPositionH8",
                    "Importar posição",
                    ModernPasteTruckPositionH8);
            ModernCopyGpsPathButtonH8 =
                CreateModernConvoyButtonH8(
                    "buttonModernCopyGpsPathH8",
                    "Copiar rota GPS",
                    ModernCopyGpsPathH8);
            ModernPasteGpsPathButtonH8 =
                CreateModernConvoyButtonH8(
                    "buttonModernPasteGpsPathH8",
                    "Importar rota GPS",
                    ModernPasteGpsPathH8);

            buttonsPanel.Controls.Add(ModernCopyTruckPositionButtonH8);
            buttonsPanel.Controls.Add(ModernPasteTruckPositionButtonH8);
            buttonsPanel.Controls.Add(ModernCopyGpsPathButtonH8);
            buttonsPanel.Controls.Add(ModernPasteGpsPathButtonH8);

            ModernConvoyStatusLabelH8 = new Label();
            ModernConvoyStatusLabelH8.Name =
                "labelModernConvoyStatusH8";
            ModernConvoyStatusLabelH8.Text =
                "As importações não salvam automaticamente.";
            ModernConvoyStatusLabelH8.Dock = DockStyle.Fill;
            ModernConvoyStatusLabelH8.Padding = new Padding(8, 4, 8, 4);
            ModernConvoyStatusLabelH8.AutoEllipsis = true;

            ModernConvoyToolsGroupH8.Controls.Add(
                ModernConvoyStatusLabelH8);
            ModernConvoyToolsGroupH8.Controls.Add(buttonsPanel);
            convoyTab.Controls.Add(ModernConvoyToolsGroupH8);
            ModernConvoyToolsGroupH8.BringToFront();
            AttachModernConvoyUiEventsH82(convoyTab);
            EnsureModernConvoyControlsEnabledH82();
        }

        private void InitializeModernConvoyToolsH8()
        {
            if (!IsModernCompatibilityMode())
                return;

            CreateModernConvoyControlsH8();
            EnsureModernConvoyControlsEnabledH82();
            ModernConvoyUiActivationH82(this, EventArgs.Empty);

            IO_Utilities.LogWriter(
                "RC5.1H8.2 modern convoy controls enabled and initialized.");
        }
    }
}