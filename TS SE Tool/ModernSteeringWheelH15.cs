using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using TS_SE_Tool.Save.Items;
using TS_SE_Tool.Utilities;

namespace TS_SE_Tool
{
    public partial class FormMain
    {
        // H18 preserves H17 and expands its explicit selector with a
        // save-derived catalog. In addition to the two H15-proven options, it
        // may expose steering wheels already installed on owned Iveco S-Way
        // trucks in the same save. Paths are constrained to the exact
        // iveco.sway steering_w namespace and conflicting refund evidence is
        // rejected. H16 keeps the single-pass graph validation and
        // Truck-panel stability fixes. H15.1 keeps the corrected job gate.
        // H15 implements the pure steering-wheel substitution proven with the
        // H15_VOLANTE_A/B ETS2 1.60 comparative saves.
        // It preserves the existing accessory id, slot arrays, paint color,
        // accessory order and every other truck accessory (including radar).
        private string ModernPendingSteeringTruckIdH15 = "";
        private string ModernPendingSteeringAccessoryIdH15 = "";
        private string ModernPendingSteeringOriginalPathH15 = "";
        private string ModernPendingSteeringTargetPathH15 = "";
        private uint ModernPendingSteeringOriginalRefundH15 = 0;
        private uint ModernPendingSteeringTargetRefundH15 = 0;
        private string ModernSteeringBaselineSavePathH15 = "";
        private DateTime ModernSteeringBaselineTimestampH15 = DateTime.MinValue;

        private const string ModernSteeringCarbonPathH15 =
            "/def/vehicle/truck/iveco.sway/accessory/steering_w/carbon.sii";
        private const string ModernSteeringAluminiumPathH15 =
            "/def/vehicle/truck/iveco.sway/accessory/steering_w/aluminium.sii";
        private const uint ModernSteeringCarbonRefundH15 = 2500;
        private const uint ModernSteeringAluminiumRefundH15 = 1800;


        private sealed class ModernSteeringOptionH17
        {
            public string Description = "";
            public string Path = "";
            public uint Refund = 0;
            public string Source = "";
            public int EvidenceCount = 0;

            public override string ToString()
            {
                return Description;
            }
        }

        private bool IsStrictModernSteeringPathH18(string path)
        {
            string normalized = NormalizeModernAccessoryPathH15(path);
            const string prefix =
                "/def/vehicle/truck/iveco.sway/accessory/steering_w/";

            return normalized.StartsWith(
                       prefix,
                       StringComparison.OrdinalIgnoreCase) &&
                   normalized.EndsWith(
                       ".sii",
                       StringComparison.OrdinalIgnoreCase) &&
                   normalized.IndexOf("..", StringComparison.Ordinal) < 0 &&
                   normalized.IndexOf('\\') < 0 &&
                   normalized.IndexOf('"') < 0 &&
                   normalized.IndexOf(':') < 0;
        }

        private string BuildModernSteeringDescriptionH18(string path)
        {
            string fileName =
                Path.GetFileNameWithoutExtension(
                    NormalizeModernAccessoryPathH15(path));

            if (String.IsNullOrWhiteSpace(fileName))
                return NormalizeModernAccessoryPathH15(path);

            string readable =
                fileName.Replace('_', ' ').Replace('-', ' ').Trim();

            if (String.IsNullOrWhiteSpace(readable))
                return NormalizeModernAccessoryPathH15(path);

            return "Volante " +
                CultureInfo.CurrentCulture.TextInfo.ToTitleCase(readable);
        }

        private List<ModernSteeringOptionH17>
            BuildModernSteeringCatalogH18(
                out int discoveredCount,
                out int conflictCount)
        {
            discoveredCount = 0;
            conflictCount = 0;

            List<ModernSteeringOptionH17> options =
                new List<ModernSteeringOptionH17>()
                {
                    new ModernSteeringOptionH17()
                    {
                        Description = "Volante de carbono",
                        Path = ModernSteeringCarbonPathH15,
                        Refund = ModernSteeringCarbonRefundH15,
                        Source = "Lista comprovada H15",
                        EvidenceCount = 0
                    },
                    new ModernSteeringOptionH17()
                    {
                        Description = "Volante de alumínio",
                        Path = ModernSteeringAluminiumPathH15,
                        Refund = ModernSteeringAluminiumRefundH15,
                        Source = "Lista comprovada H15",
                        EvidenceCount = 0
                    }
                };

            Dictionary<string, HashSet<uint>> refundsByPath =
                new Dictionary<string, HashSet<uint>>(
                    StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> evidenceByPath =
                new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);

            if (SiiNunitData != null &&
                SiiNunitData.Player != null)
            {
                foreach (string rawTruckId in
                         SiiNunitData.Player.trucks ??
                         new List<string>())
                {
                    string truckId =
                        NormalizeModernReference(rawTruckId);

                    if (!IsOwnedModernTruckH6(truckId) ||
                        !SiiNunitData.SiiNitems.ContainsKey(truckId))
                    {
                        continue;
                    }

                    Vehicle truck =
                        SiiNunitData.SiiNitems[truckId] as Vehicle;

                    if (truck == null || truck.accessories == null)
                        continue;

                    foreach (string rawAccessoryId in truck.accessories)
                    {
                        string accessoryId =
                            NormalizeModernReference(rawAccessoryId);

                        if (String.IsNullOrWhiteSpace(accessoryId) ||
                            !SiiNunitData.SiiNitems.ContainsKey(accessoryId))
                        {
                            continue;
                        }

                        Vehicle_Addon_Accessory addon =
                            SiiNunitData.SiiNitems[accessoryId]
                                as Vehicle_Addon_Accessory;

                        if (addon == null)
                            continue;

                        string path =
                            NormalizeModernAccessoryPathH15(
                                addon.data_path);

                        if (!IsStrictModernSteeringPathH18(path))
                            continue;

                        if (!refundsByPath.ContainsKey(path))
                        {
                            refundsByPath[path] =
                                new HashSet<uint>();
                            evidenceByPath[path] = 0;
                        }

                        refundsByPath[path].Add(addon.refund);
                        evidenceByPath[path]++;
                    }
                }
            }

            foreach (KeyValuePair<string, HashSet<uint>> item in
                     refundsByPath.OrderBy(
                         pair => pair.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                ModernSteeringOptionH17 existing =
                    options.FirstOrDefault(option =>
                        option.Path.Equals(
                            item.Key,
                            StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    existing.EvidenceCount = evidenceByPath[item.Key];
                    existing.Source =
                        "Lista comprovada H15; também detectado na frota";
                    continue;
                }

                if (item.Value.Count != 1)
                {
                    conflictCount++;
                    continue;
                }

                options.Add(
                    new ModernSteeringOptionH17()
                    {
                        Description =
                            BuildModernSteeringDescriptionH18(item.Key),
                        Path = item.Key,
                        Refund = item.Value.First(),
                        Source =
                            "Detectado em caminhão próprio no save",
                        EvidenceCount = evidenceByPath[item.Key]
                    });
                discoveredCount++;
            }

            return options;
        }

        private List<ModernSteeringOptionH17>
            GetCompatibleModernSteeringOptionsH17()
        {
            int discoveredCount;
            int conflictCount;

            return BuildModernSteeringCatalogH18(
                out discoveredCount,
                out conflictCount);
        }

        private ModernSteeringOptionH17
            FindCompatibleModernSteeringOptionH17(string path)
        {
            string normalized = NormalizeModernAccessoryPathH15(path);

            return GetCompatibleModernSteeringOptionsH17()
                .FirstOrDefault(option =>
                    option.Path.Equals(
                        normalized,
                        StringComparison.OrdinalIgnoreCase));
        }

        private bool HasPendingModernSteeringWheelChangeH15()
        {
            return !String.IsNullOrWhiteSpace(
                ModernPendingSteeringAccessoryIdH15);
        }

        private void ResetModernSteeringWheelStateH15()
        {
            ModernPendingSteeringTruckIdH15 = "";
            ModernPendingSteeringAccessoryIdH15 = "";
            ModernPendingSteeringOriginalPathH15 = "";
            ModernPendingSteeringTargetPathH15 = "";
            ModernPendingSteeringOriginalRefundH15 = 0;
            ModernPendingSteeringTargetRefundH15 = 0;
            ModernSteeringBaselineSavePathH15 = "";
            ModernSteeringBaselineTimestampH15 = DateTime.MinValue;
        }

        private bool ModernSteeringBaselineIsCurrentH15()
        {
            return !String.IsNullOrWhiteSpace(
                       ModernSteeringBaselineSavePathH15) &&
                   String.Equals(
                       ModernSteeringBaselineSavePathH15,
                       Globals.SelectedSavePath ?? "",
                       StringComparison.OrdinalIgnoreCase) &&
                   ModernSteeringBaselineTimestampH15 ==
                       LastModifiedTimestamp;
        }

        private string NormalizeModernAccessoryPathH15(string value)
        {
            string normalized = (value ?? "").Trim();

            if (normalized.Length >= 2 &&
                normalized[0] == '"' &&
                normalized[normalized.Length - 1] == '"')
            {
                normalized = normalized.Substring(
                    1,
                    normalized.Length - 2);
            }

            return normalized;
        }

        private bool IsSupportedModernSteeringPathH15(string path)
        {
            return FindCompatibleModernSteeringOptionH17(path) != null;
        }

        private string GetModernSteeringDescriptionH15(string path)
        {
            ModernSteeringOptionH17 option =
                FindCompatibleModernSteeringOptionH17(path);

            return option != null
                ? option.Description
                : NormalizeModernAccessoryPathH15(path);
        }

        private bool TryGetModernSteeringWheelAccessoryH15(
            out string accessoryId,
            out Vehicle_Addon_Accessory accessory,
            out string reason)
        {
            accessoryId = "";
            accessory = null;
            reason = "";

            ResolveModernActiveVehicleReferences();

            if (String.IsNullOrWhiteSpace(ModernActiveTruckId) ||
                !IsModernTruckObject(ModernActiveTruckId))
            {
                reason = "O caminhão ativo moderno não foi encontrado.";
                return false;
            }

            Vehicle truck =
                (Vehicle)SiiNunitData.SiiNitems[ModernActiveTruckId];

            if (truck.accessories == null ||
                truck.accessories.Count == 0)
            {
                reason = "O caminhão ativo não possui acessórios válidos.";
                return false;
            }

            List<KeyValuePair<string, Vehicle_Addon_Accessory>> candidates =
                new List<KeyValuePair<string, Vehicle_Addon_Accessory>>();

            foreach (string rawAccessoryId in truck.accessories)
            {
                string normalizedId =
                    NormalizeModernReference(rawAccessoryId);

                if (String.IsNullOrWhiteSpace(normalizedId) ||
                    !SiiNunitData.SiiNitems.ContainsKey(normalizedId))
                {
                    continue;
                }

                dynamic rawAccessory =
                    SiiNunitData.SiiNitems[normalizedId];

                if (!(rawAccessory is Vehicle_Addon_Accessory))
                    continue;

                Vehicle_Addon_Accessory addon =
                    (Vehicle_Addon_Accessory)rawAccessory;
                string path =
                    NormalizeModernAccessoryPathH15(addon.data_path);

                if (path.IndexOf(
                        "/accessory/steering_w/",
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                candidates.Add(
                    new KeyValuePair<string, Vehicle_Addon_Accessory>(
                        normalizedId,
                        addon));
            }

            if (candidates.Count != 1)
            {
                reason =
                    "H15 exige exatamente um volante no caminhão ativo. " +
                    "Encontrados: " +
                    candidates.Count.ToString(CultureInfo.InvariantCulture) +
                    ".";
                return false;
            }

            accessoryId = candidates[0].Key;
            accessory = candidates[0].Value;

            if (!IsSupportedModernSteeringPathH15(accessory.data_path))
            {
                reason =
                    "A H18 aceita somente volantes comprovados pela lista " +
                    "H15 ou detectados em caminhões Iveco S-Way próprios " +
                    "deste save.";
                return false;
            }

            return true;
        }

        private bool HasOtherPendingModernChangesH15(out string reason)
        {
            reason = "";

            if (HasPendingModernDriverSkillsH20())
            {
                reason = "Existem alterações de habilidades de motorista pendentes.";
                return true;
            }

            if (HasPendingModernTrailerSwitchH14())
            {
                reason = "Existe uma troca de reboque preparada.";
                return true;
            }

            if (!String.IsNullOrWhiteSpace(
                    ModernPendingTruckSwitchTargetTruckId))
            {
                reason = "Existe uma troca de caminhão preparada.";
                return true;
            }

            if (HasUnsavedModernVehicleChanges())
            {
                reason =
                    "Existem alterações de manutenção, combustível, placa, " +
                    "odômetro, massa ou frota ainda não gravadas.";
                return true;
            }

            if (HasUnsavedModernGarageChangesH10() ||
                HasUnsavedModernHeadquartersChangesH12())
            {
                reason = "Existem alterações de garagem ou cidade-sede pendentes.";
                return true;
            }

            if (HasUnsavedModernVisitedCityChangesH10())
            {
                reason = "Existem alterações de cidades visitadas pendentes.";
                return true;
            }

            if (HasUnsavedModernMarketChangesH13R())
            {
                reason = "Existem alterações dos mercados pendentes.";
                return true;
            }

            if (ModernConvoyStatusLabelH8 != null &&
                ModernConvoyStatusLabelH8.Text != null &&
                ModernConvoyStatusLabelH8.Text.IndexOf(
                    "em memória",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reason = "Existe uma importação de posição ou rota pendente.";
                return true;
            }

            return false;
        }

        private void SetModernSteeringPendingLockH15(bool locked)
        {
            if (!locked)
                return;

            if (comboBoxUserTruckCompanyTrucks != null)
                comboBoxUserTruckCompanyTrucks.Enabled = false;

            if (buttonUserTruckSelectCurrent != null)
                buttonUserTruckSelectCurrent.Enabled = false;

            if (buttonUserTruckSwitchCurrent != null)
                buttonUserTruckSwitchCurrent.Enabled = false;

            if (ModernTruckAdvancedPanel != null)
                ModernTruckAdvancedPanel.Enabled = false;

            foreach (TabPage page in tabControlMain.TabPages)
                page.Enabled = page == tabPageTruck;
        }

        private void UpdateModernSteeringWheelUiH15()
        {
            if (!IsModernCompatibilityMode())
                return;

            Control editor =
                tabPageTruck.Controls
                    .Find("buttonTruckVehicleEditor", true)
                    .FirstOrDefault();

            if (editor == null)
                return;

            bool pending = HasPendingModernSteeringWheelChangeH15();
            string accessoryId;
            Vehicle_Addon_Accessory accessory;
            string reason;
            bool supported =
                TryGetModernSteeringWheelAccessoryH15(
                    out accessoryId,
                    out accessory,
                    out reason);

            editor.Enabled = !pending && supported;

            if (pending)
            {
                toolTipMain.SetToolTip(
                    editor,
                    "Troca de volante preparada. Clique em Salvar e recarregue o save.");
                SetModernSteeringPendingLockH15(true);
                return;
            }

            if (supported)
            {
                string current =
                    GetModernSteeringDescriptionH15(accessory.data_path);

                toolTipMain.SetToolTip(
                    editor,
                    "H18 — catálogo seguro de volantes. Atual: " +
                    current + ".");
            }
            else
            {
                toolTipMain.SetToolTip(editor, reason);
            }
        }

        private bool CanOpenModernSteeringWheelEditorH15()
        {
            if (!IsModernCompatibilityMode() ||
                HasPendingModernSteeringWheelChangeH15())
            {
                return false;
            }

            string accessoryId;
            Vehicle_Addon_Accessory accessory;
            string reason;

            return TryGetModernSteeringWheelAccessoryH15(
                out accessoryId,
                out accessory,
                out reason);
        }

        private bool TrySelectModernSteeringOptionH17(
            string currentPath,
            uint currentRefund,
            out string targetPath,
            out uint targetRefund)
        {
            targetPath = "";
            targetRefund = 0;

            ModernSteeringOptionH17 currentOption =
                FindCompatibleModernSteeringOptionH17(currentPath);
            List<ModernSteeringOptionH17> options =
                GetCompatibleModernSteeringOptionsH17();

            if (currentOption == null || options.Count < 2)
            {
                MessageBox.Show(
                    "Não existem opções H18 suficientes para este volante.",
                    "H18 — seletor indisponível",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            using (Form dialog = new Form())
            {
                dialog.Text = "H18 — selecionar volante compatível";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.AutoScaleMode = AutoScaleMode.Font;
                dialog.ClientSize = new Size(720, 330);

                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.Padding = new Padding(14);
                layout.ColumnCount = 1;
                layout.RowCount = 5;
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                dialog.Controls.Add(layout);

                Label currentLabel = new Label();
                currentLabel.AutoSize = true;
                currentLabel.Margin = new Padding(0, 0, 0, 12);
                currentLabel.Text =
                    "Volante atual: " + currentOption.Description +
                    Environment.NewLine +
                    "Caminho atual: " + currentOption.Path +
                    Environment.NewLine +
                    "Reembolso atual: " +
                    currentRefund.ToString(CultureInfo.InvariantCulture);
                layout.Controls.Add(currentLabel, 0, 0);

                Label selectLabel = new Label();
                selectLabel.AutoSize = true;
                selectLabel.Text = "Selecione a opção compatível:";
                selectLabel.Margin = new Padding(0, 0, 0, 4);
                layout.Controls.Add(selectLabel, 0, 1);

                ComboBox combo = new ComboBox();
                combo.DropDownStyle = ComboBoxStyle.DropDownList;
                combo.Dock = DockStyle.Top;
                combo.Margin = new Padding(0, 0, 0, 10);
                foreach (ModernSteeringOptionH17 option in options)
                    combo.Items.Add(option);
                combo.SelectedItem = options.First(option =>
                    option.Path.Equals(
                        currentOption.Path,
                        StringComparison.OrdinalIgnoreCase));
                layout.Controls.Add(combo, 0, 2);

                TextBox details = new TextBox();
                details.Multiline = true;
                details.ReadOnly = true;
                details.ScrollBars = ScrollBars.Vertical;
                details.Dock = DockStyle.Fill;
                details.BackColor = SystemColors.Window;
                details.Margin = new Padding(0, 0, 0, 12);
                layout.Controls.Add(details, 0, 3);

                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.Dock = DockStyle.Fill;
                buttons.AutoSize = true;
                buttons.WrapContents = false;
                buttons.Margin = new Padding(0);
                layout.Controls.Add(buttons, 0, 4);

                Button cancelButton = new Button();
                cancelButton.Text = "Cancelar";
                cancelButton.DialogResult = DialogResult.Cancel;
                cancelButton.AutoSize = true;
                cancelButton.Margin = new Padding(8, 0, 0, 0);
                buttons.Controls.Add(cancelButton);

                Button confirmButton = new Button();
                confirmButton.Text = "Selecionar";
                confirmButton.DialogResult = DialogResult.OK;
                confirmButton.AutoSize = true;
                buttons.Controls.Add(confirmButton);

                Action updateDetails = delegate()
                {
                    ModernSteeringOptionH17 selected =
                        combo.SelectedItem as ModernSteeringOptionH17;
                    bool changed =
                        selected != null &&
                        !selected.Path.Equals(
                            currentOption.Path,
                            StringComparison.OrdinalIgnoreCase);

                    confirmButton.Enabled = changed;
                    details.Text = selected == null
                        ? "Nenhuma opção selecionada."
                        : "Opção: " + selected.Description +
                          Environment.NewLine +
                          "Caminho permitido: " + selected.Path +
                          Environment.NewLine +
                          "Reembolso gravado: " +
                          selected.Refund.ToString(
                              CultureInfo.InvariantCulture) +
                          Environment.NewLine +
                          "Origem da opção: " + selected.Source +
                          Environment.NewLine +
                          "Ocorrências compatíveis na frota: " +
                          selected.EvidenceCount.ToString(
                              CultureInfo.InvariantCulture) +
                          Environment.NewLine + Environment.NewLine +
                          "A H18 altera somente data_path e refund no " +
                          "mesmo bloco _nameless. A lista e todos os " +
                          "demais acessórios serão preservados.";
                };

                combo.SelectedIndexChanged += delegate
                {
                    updateDetails();
                };
                updateDetails();

                dialog.AcceptButton = confirmButton;
                dialog.CancelButton = cancelButton;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return false;

                ModernSteeringOptionH17 selectedOption =
                    combo.SelectedItem as ModernSteeringOptionH17;
                if (selectedOption == null ||
                    selectedOption.Path.Equals(
                        currentOption.Path,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                targetPath = selectedOption.Path;
                targetRefund = selectedOption.Refund;

                IO_Utilities.LogWriter(
                    "RC5.1H18 steering selector: current=" +
                    currentOption.Path +
                    ", selected=" + targetPath +
                    ", refund=" +
                    currentRefund.ToString(
                        CultureInfo.InvariantCulture) +
                    " -> " +
                    targetRefund.ToString(
                        CultureInfo.InvariantCulture) +
                    ", source=" + selectedOption.Source +
                    ", evidence_count=" +
                    selectedOption.EvidenceCount.ToString(
                        CultureInfo.InvariantCulture) +
                    ", result=selected.");

                return true;
            }
        }

        private void ShowModernSteeringWheelEditorH15()
        {
            if (!IsModernCompatibilityMode())
                return;

            if (HasPendingModernSteeringWheelChangeH15())
            {
                MessageBox.Show(
                    "A troca de volante já foi preparada. " +
                    "Clique em Salvar e recarregue o save.",
                    "H15 — alteração pendente",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string accessoryId;
            Vehicle_Addon_Accessory accessory;
            string reason;

            if (!TryGetModernSteeringWheelAccessoryH15(
                    out accessoryId,
                    out accessory,
                    out reason))
            {
                MessageBox.Show(
                    reason,
                    "H15 — troca de volante bloqueada",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                UpdateModernSteeringWheelUiH15();
                return;
            }

            string currentJobH15 =
                NormalizeModernReference(SiiNunitData.Player.current_job);
            string selectedJobH15 =
                NormalizeModernReference(SiiNunitData.Player.selected_job);
            bool hasCurrentOrSelectedJobH15 =
                !String.IsNullOrWhiteSpace(currentJobH15) ||
                !String.IsNullOrWhiteSpace(selectedJobH15);

            IO_Utilities.LogWriter(
                "RC5.1H15.1 job gate: current_job=" +
                (String.IsNullOrWhiteSpace(currentJobH15)
                    ? "<none>"
                    : currentJobH15) +
                ", selected_job=" +
                (String.IsNullOrWhiteSpace(selectedJobH15)
                    ? "<none>"
                    : selectedJobH15) +
                ", result=" +
                (hasCurrentOrSelectedJobH15 ? "blocked" : "allowed") +
                ".");

            if (hasCurrentOrSelectedJobH15)
            {
                MessageBox.Show(
                    "A troca de volante exige que não exista trabalho atual " +
                    "nem trabalho selecionado.",
                    "H15 — trabalho ativo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string pendingReason;
            if (HasOtherPendingModernChangesH15(out pendingReason))
            {
                MessageBox.Show(
                    pendingReason + Environment.NewLine +
                    "Salve, recarregue o save e tente novamente.",
                    "H15 — salve as alterações primeiro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string originalPath =
                NormalizeModernAccessoryPathH15(accessory.data_path);
            string targetPath;
            uint targetRefund;

            if (!TrySelectModernSteeringOptionH17(
                    originalPath,
                    accessory.refund,
                    out targetPath,
                    out targetRefund))
            {
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                "Caminhão ativo:" + Environment.NewLine +
                ModernActiveTruckId + Environment.NewLine +
                Environment.NewLine +
                "Acessório preservado:" + Environment.NewLine +
                accessoryId + Environment.NewLine +
                Environment.NewLine +
                "Troca:" + Environment.NewLine +
                GetModernSteeringDescriptionH15(originalPath) +
                " → " +
                GetModernSteeringDescriptionH15(targetPath) +
                Environment.NewLine +
                Environment.NewLine +
                "A H18 altera somente data_path e refund no mesmo bloco. " +
                "A lista, os IDs e todos os demais acessórios — inclusive " +
                "o radar — serão preservados." +
                Environment.NewLine + Environment.NewLine +
                "Preparar esta alteração?",
                "H18 — confirmar volante selecionado",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirmation != DialogResult.Yes)
                return;

            ModernPendingSteeringTruckIdH15 = ModernActiveTruckId;
            ModernPendingSteeringAccessoryIdH15 = accessoryId;
            ModernPendingSteeringOriginalPathH15 = originalPath;
            ModernPendingSteeringTargetPathH15 = targetPath;
            ModernPendingSteeringOriginalRefundH15 = accessory.refund;
            ModernPendingSteeringTargetRefundH15 = targetRefund;
            ModernSteeringBaselineSavePathH15 =
                Globals.SelectedSavePath ?? "";
            ModernSteeringBaselineTimestampH15 =
                LastModifiedTimestamp;

            IO_Utilities.LogWriter(
                "RC5.1H15 staged steering wheel change: truck=" +
                ModernPendingSteeringTruckIdH15 +
                ", accessory=" +
                ModernPendingSteeringAccessoryIdH15 +
                ", path=" +
                ModernPendingSteeringOriginalPathH15 +
                " -> " +
                ModernPendingSteeringTargetPathH15 +
                ", refund=" +
                ModernPendingSteeringOriginalRefundH15.ToString(
                    CultureInfo.InvariantCulture) +
                " -> " +
                ModernPendingSteeringTargetRefundH15.ToString(
                    CultureInfo.InvariantCulture) +
                ".");

            UpdateModernSteeringWheelUiH15();

            MessageBox.Show(
                "A troca de volante foi preparada em memória." +
                Environment.NewLine +
                "Clique em Salvar e depois recarregue o mesmo save.",
                "H18 — alteração preparada",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private sealed class ModernSteeringGraphSnapshotH16
        {
            public List<string> AccessoryIds =
                new List<string>();
            public int TargetUnitCount = 0;
            public int SteeringAccessoryCount = 0;
            public string TargetPathRaw = "";
            public string TargetRefundRaw = "";
        }

        private bool TryReadModernUIntH15(
            string raw,
            out uint value)
        {
            return UInt32.TryParse(
                (raw ?? "").Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        private string ExtractModernUnitHeaderIdH16(
            string trimmedLine,
            string headerPrefix)
        {
            string rawId =
                trimmedLine.Substring(headerPrefix.Length).Trim();
            int braceIndex = rawId.IndexOf('{');

            if (braceIndex >= 0)
                rawId = rawId.Substring(0, braceIndex).Trim();

            return NormalizeModernReference(rawId);
        }

        private ModernSteeringGraphSnapshotH16
            AnalyzeModernSteeringGraphH16(
                string[] lines,
                string truckId,
                string targetAccessoryId)
        {
            ModernSteeringGraphSnapshotH16 snapshot =
                new ModernSteeringGraphSnapshotH16();

            snapshot.AccessoryIds =
                GetModernUnitArrayValues(
                    lines,
                    "vehicle",
                    truckId,
                    "accessories");

            HashSet<string> ownedAccessoryIds =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string rawAccessoryId in snapshot.AccessoryIds)
            {
                string normalizedAccessoryId =
                    NormalizeModernReference(rawAccessoryId);

                if (!String.IsNullOrWhiteSpace(normalizedAccessoryId))
                    ownedAccessoryIds.Add(normalizedAccessoryId);
            }

            string normalizedTargetId =
                NormalizeModernReference(targetAccessoryId);
            const string accessoryHeader =
                "vehicle_addon_accessory : ";

            bool insideAccessory = false;
            bool currentOwned = false;
            bool currentTarget = false;

            foreach (string rawLine in lines)
            {
                string line = (rawLine ?? "").Trim();

                if (!insideAccessory)
                {
                    if (!line.StartsWith(
                            accessoryHeader,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string currentId =
                        ExtractModernUnitHeaderIdH16(
                            line,
                            accessoryHeader);

                    insideAccessory = true;
                    currentOwned =
                        ownedAccessoryIds.Contains(currentId);
                    currentTarget =
                        currentId.Equals(
                            normalizedTargetId,
                            StringComparison.OrdinalIgnoreCase);

                    if (currentTarget)
                        snapshot.TargetUnitCount++;

                    continue;
                }

                if (line == "}")
                {
                    insideAccessory = false;
                    currentOwned = false;
                    currentTarget = false;
                    continue;
                }

                if (!currentOwned && !currentTarget)
                    continue;

                int separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                    continue;

                string fieldName =
                    line.Substring(0, separatorIndex).Trim();
                string fieldValue =
                    line.Substring(separatorIndex + 1).Trim();

                if (fieldName.Equals(
                        "data_path",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (currentOwned &&
                        NormalizeModernAccessoryPathH15(fieldValue)
                            .IndexOf(
                                "/accessory/steering_w/",
                                StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        snapshot.SteeringAccessoryCount++;
                    }

                    if (currentTarget)
                        snapshot.TargetPathRaw = fieldValue;
                }
                else if (currentTarget &&
                         fieldName.Equals(
                             "refund",
                             StringComparison.OrdinalIgnoreCase))
                {
                    snapshot.TargetRefundRaw = fieldValue;
                }
            }

            return snapshot;
        }

        private int CountTargetAccessoryReferencesH16(
            IList<string> accessoryIds,
            string targetAccessoryId)
        {
            string normalizedTargetId =
                NormalizeModernReference(targetAccessoryId);

            return accessoryIds.Count(rawId =>
                NormalizeModernReference(rawId).Equals(
                    normalizedTargetId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private string PatchModernSteeringWheelDataH15(string content)
        {
            if (!HasPendingModernSteeringWheelChangeH15())
                return content;

            Stopwatch watch = Stopwatch.StartNew();

            if (!ModernSteeringBaselineIsCurrentH15())
            {
                throw new InvalidOperationException(
                    "H15 steering-wheel baseline is unavailable or stale. " +
                    "Reload the save before writing.");
            }

            ResolveModernActiveVehicleReferences();

            if (!ModernActiveTruckId.Equals(
                    ModernPendingSteeringTruckIdH15,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "H15 rejected the write because the active truck changed.");
            }

            if (!IsStrictModernSteeringPathH18(
                    ModernPendingSteeringTargetPathH15) ||
                FindCompatibleModernSteeringOptionH17(
                    ModernPendingSteeringTargetPathH15) == null)
            {
                throw new InvalidDataException(
                    "H18 rejected a steering-wheel target outside the " +
                    "validated save-derived catalog.");
            }

            string[] beforeLines = content
                .Replace("\r\n", "\n")
                .Split(new char[] { '\n' });

            ModernSteeringGraphSnapshotH16 before =
                AnalyzeModernSteeringGraphH16(
                    beforeLines,
                    ModernPendingSteeringTruckIdH15,
                    ModernPendingSteeringAccessoryIdH15);

            uint originalRefund;

            if (before.AccessoryIds.Count == 0 ||
                CountTargetAccessoryReferencesH16(
                    before.AccessoryIds,
                    ModernPendingSteeringAccessoryIdH15) != 1 ||
                before.SteeringAccessoryCount != 1 ||
                before.TargetUnitCount != 1)
            {
                throw new InvalidDataException(
                    "H15 rejected the write because the steering-wheel graph " +
                    "does not match the proven topology.");
            }

            if (!TryReadModernUIntH15(
                    before.TargetRefundRaw,
                    out originalRefund) ||
                !NormalizeModernAccessoryPathH15(
                    before.TargetPathRaw).Equals(
                        ModernPendingSteeringOriginalPathH15,
                        StringComparison.OrdinalIgnoreCase) ||
                originalRefund != ModernPendingSteeringOriginalRefundH15)
            {
                throw new InvalidDataException(
                    "H15 rejected the write because the original steering-wheel " +
                    "block changed after staging.");
            }

            string output = PatchModernUnitFields(
                content,
                "vehicle_addon_accessory",
                ModernPendingSteeringAccessoryIdH15,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    {
                        "data_path",
                        TextUtilities.FromStringToOutputString(
                            ModernPendingSteeringTargetPathH15)
                    },
                    {
                        "refund",
                        ModernPendingSteeringTargetRefundH15.ToString(
                            CultureInfo.InvariantCulture)
                    }
                },
                null);

            string[] afterLines = output
                .Replace("\r\n", "\n")
                .Split(new char[] { '\n' });

            ModernSteeringGraphSnapshotH16 after =
                AnalyzeModernSteeringGraphH16(
                    afterLines,
                    ModernPendingSteeringTruckIdH15,
                    ModernPendingSteeringAccessoryIdH15);

            uint targetRefund;
            bool targetValid =
                TryReadModernUIntH15(
                    after.TargetRefundRaw,
                    out targetRefund) &&
                NormalizeModernAccessoryPathH15(
                    after.TargetPathRaw).Equals(
                        ModernPendingSteeringTargetPathH15,
                        StringComparison.OrdinalIgnoreCase) &&
                targetRefund == ModernPendingSteeringTargetRefundH15;

            if (!targetValid ||
                !before.AccessoryIds.SequenceEqual(
                    after.AccessoryIds,
                    StringComparer.OrdinalIgnoreCase) ||
                CountTargetAccessoryReferencesH16(
                    after.AccessoryIds,
                    ModernPendingSteeringAccessoryIdH15) != 1 ||
                after.SteeringAccessoryCount != 1 ||
                after.TargetUnitCount != 1)
            {
                throw new InvalidDataException(
                    "H15 post-write validation rejected the steering-wheel change.");
            }

            watch.Stop();

            IO_Utilities.LogWriter(
                "RC5.1H16 steering validation: elapsed_ms=" +
                watch.ElapsedMilliseconds.ToString(
                    CultureInfo.InvariantCulture) +
                ", accessory_count=" +
                after.AccessoryIds.Count.ToString(
                    CultureInfo.InvariantCulture) +
                ", result=allowed.");

            IO_Utilities.LogWriter(
                "RC5.1H15 steering wheel change validated: truck=" +
                ModernPendingSteeringTruckIdH15 +
                ", accessory=" +
                ModernPendingSteeringAccessoryIdH15 +
                ", path=" +
                ModernPendingSteeringOriginalPathH15 +
                " -> " +
                ModernPendingSteeringTargetPathH15 +
                ", refund=" +
                ModernPendingSteeringOriginalRefundH15.ToString(
                    CultureInfo.InvariantCulture) +
                " -> " +
                ModernPendingSteeringTargetRefundH15.ToString(
                    CultureInfo.InvariantCulture) +
                ", accessory_list=preserved, elapsed_ms=" +
                watch.ElapsedMilliseconds.ToString(
                    CultureInfo.InvariantCulture) +
                ".");

            return output;
        }

        private void InitializeModernSteeringWheelH15()
        {
            ResetModernSteeringWheelStateH15();
            UpdateModernSteeringWheelUiH15();

            IO_Utilities.LogWriter(
                "RC5.1H15 controlled Iveco S-Way steering-wheel substitution initialized.");
            IO_Utilities.LogWriter(
                "RC5.1H16 single-pass steering validation and Truck panel stability initialized.");
            IO_Utilities.LogWriter(
                "RC5.1H17 explicit steering selector foundation initialized.");

            int discoveredCountH18;
            int conflictCountH18;
            List<ModernSteeringOptionH17> catalogH18 =
                BuildModernSteeringCatalogH18(
                    out discoveredCountH18,
                    out conflictCountH18);

            IO_Utilities.LogWriter(
                "RC5.1H18 steering catalog initialized: proven=2, discovered=" +
                discoveredCountH18.ToString(
                    CultureInfo.InvariantCulture) +
                ", total=" +
                catalogH18.Count.ToString(
                    CultureInfo.InvariantCulture) +
                ", rejected_conflicts=" +
                conflictCountH18.ToString(
                    CultureInfo.InvariantCulture) +
                ".");
        }
    }
}
