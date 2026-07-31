using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using TS_SE_Tool.Save.Items;
using TS_SE_Tool.Utilities;

namespace TS_SE_Tool
{
    public partial class FormMain
    {
        // H20 completes the safe modern workflow for AI-driver skills and
        // exposes an explicit resource matrix. Graph mutations such as
        // hiring/firing remain blocked until their cross-reference writer is
        // proven. The extracted-def catalog is read-only and never feeds an
        // unverified path into the accessory writer.
        private sealed class ModernDriverSkillsSnapshotH20
        {
            public byte Adr;
            public byte LongDistance;
            public byte Heavy;
            public byte Fragile;
            public byte Urgent;
            public byte Mechanical;

            public bool EqualsDriver(Driver_AI driver)
            {
                return driver != null &&
                    Adr == driver.adr &&
                    LongDistance == driver.long_dist &&
                    Heavy == driver.heavy &&
                    Fragile == driver.fragile &&
                    Urgent == driver.urgent &&
                    Mechanical == driver.mechanical;
            }

            public string Describe()
            {
                return "adr=" + Adr.ToString(CultureInfo.InvariantCulture) +
                    ", long_dist=" + LongDistance.ToString(CultureInfo.InvariantCulture) +
                    ", heavy=" + Heavy.ToString(CultureInfo.InvariantCulture) +
                    ", fragile=" + Fragile.ToString(CultureInfo.InvariantCulture) +
                    ", urgent=" + Urgent.ToString(CultureInfo.InvariantCulture) +
                    ", mechanical=" + Mechanical.ToString(CultureInfo.InvariantCulture);
            }
        }

        private readonly Dictionary<string, ModernDriverSkillsSnapshotH20>
            ModernDriverSkillBaselinesH20 =
                new Dictionary<string, ModernDriverSkillsSnapshotH20>(
                    StringComparer.OrdinalIgnoreCase);

        private string ModernDriverBaselineSavePathH20 = "";
        private DateTime ModernDriverBaselineTimestampH20 = DateTime.MinValue;
        private bool ModernDriverEventsAttachedH20 = false;
        private ToolStripMenuItem ModernFeatureCenterMenuH20 = null;
        private const string CommunityInstagramUrlH21 =
            "https://www.instagram.com/fabio.meskita/";

        private ModernDriverSkillsSnapshotH20 CaptureModernDriverSkillsH20(
            Driver_AI driver)
        {
            if (driver == null)
                return null;

            return new ModernDriverSkillsSnapshotH20()
            {
                Adr = driver.adr,
                LongDistance = driver.long_dist,
                Heavy = driver.heavy,
                Fragile = driver.fragile,
                Urgent = driver.urgent,
                Mechanical = driver.mechanical
            };
        }

        private bool ModernDriverBaselineIsCurrentH20()
        {
            return ModernDriverSkillBaselinesH20.Count > 0 &&
                String.Equals(
                    ModernDriverBaselineSavePathH20,
                    Globals.SelectedSavePath ?? "",
                    StringComparison.OrdinalIgnoreCase) &&
                ModernDriverBaselineTimestampH20 == LastModifiedTimestamp;
        }

        private void CaptureModernDriverSkillBaselinesH20()
        {
            string currentPath = Globals.SelectedSavePath ?? "";
            if (ModernDriverSkillBaselinesH20.Count > 0 &&
                String.Equals(
                    ModernDriverBaselineSavePathH20,
                    currentPath,
                    StringComparison.OrdinalIgnoreCase) &&
                ModernDriverBaselineTimestampH20 == LastModifiedTimestamp)
            {
                return;
            }

            ModernDriverSkillBaselinesH20.Clear();
            ModernDriverBaselineSavePathH20 = currentPath;
            ModernDriverBaselineTimestampH20 = LastModifiedTimestamp;

            if (SiiNunitData == null || SiiNunitData.SiiNitems == null)
                return;

            foreach (string id in SiiNunitData.SiiNitems.Keys.ToArray())
            {
                object item = (object)SiiNunitData.SiiNitems[id];
                Driver_AI driver = item as Driver_AI;
                if (driver == null)
                    continue;

                ModernDriverSkillBaselinesH20[id] =
                    CaptureModernDriverSkillsH20(driver);
            }
        }

        private List<string> GetPendingModernDriverSkillIdsH20()
        {
            List<string> pending = new List<string>();
            if (!ModernDriverBaselineIsCurrentH20() ||
                SiiNunitData == null ||
                SiiNunitData.SiiNitems == null)
            {
                return pending;
            }

            foreach (KeyValuePair<string, ModernDriverSkillsSnapshotH20> item in
                     ModernDriverSkillBaselinesH20)
            {
                if (!SiiNunitData.SiiNitems.ContainsKey(item.Key))
                    continue;

                Driver_AI current =
                    (object)SiiNunitData.SiiNitems[item.Key] as Driver_AI;
                if (current != null && !item.Value.EqualsDriver(current))
                    pending.Add(item.Key);
            }

            pending.Sort(StringComparer.OrdinalIgnoreCase);
            return pending;
        }

        private bool HasPendingModernDriverSkillsH20()
        {
            return GetPendingModernDriverSkillIdsH20().Count > 0;
        }

        private bool TryParseModernDriverByteH20(
            string raw,
            out byte value)
        {
            value = 0;
            string normalized = (raw ?? "").Trim().Trim('"');
            return Byte.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        private int CountModernUnitHeadersH20(
            string content,
            string unitType,
            string unitId)
        {
            int count = 0;
            string[] lines = (content ?? "")
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n');

            foreach (string line in lines)
            {
                string foundType;
                string foundId;
                if (TryParseModernUnitHeader(line, out foundType, out foundId) &&
                    foundType.Equals(unitType, StringComparison.Ordinal) &&
                    foundId.Equals(unitId, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
            return count;
        }

        private void ValidateModernDriverSkillRangesH20(
            string driverId,
            Driver_AI driver)
        {
            if (driver == null)
                throw new InvalidDataException("H20 driver object is unavailable: " + driverId);

            if (driver.adr > 63 ||
                driver.long_dist > 6 ||
                driver.heavy > 6 ||
                driver.fragile > 6 ||
                driver.urgent > 6 ||
                driver.mechanical > 6)
            {
                throw new InvalidDataException(
                    "H20 rejected invalid driver-skill ranges for " + driverId + ".");
            }
        }

        private void ValidateModernDriverFieldsAgainstSnapshotH20(
            string[] lines,
            string driverId,
            ModernDriverSkillsSnapshotH20 expected,
            string phase)
        {
            Dictionary<string, byte> expectedValues =
                new Dictionary<string, byte>(StringComparer.Ordinal)
                {
                    { "adr", expected.Adr },
                    { "long_dist", expected.LongDistance },
                    { "heavy", expected.Heavy },
                    { "fragile", expected.Fragile },
                    { "urgent", expected.Urgent },
                    { "mechanical", expected.Mechanical }
                };

            foreach (KeyValuePair<string, byte> field in expectedValues)
            {
                string raw;
                byte actual;
                if (!TryGetModernUnitField(
                        lines,
                        "driver_ai",
                        driverId,
                        field.Key,
                        out raw) ||
                    !TryParseModernDriverByteH20(raw, out actual) ||
                    actual != field.Value)
                {
                    throw new InvalidDataException(
                        "H20 " + phase + " validation rejected driver " +
                        driverId + " field " + field.Key + ".");
                }
            }
        }

        private string PatchModernDriverSkillsDataH20(string content)
        {
            List<string> pending = GetPendingModernDriverSkillIdsH20();
            if (pending.Count == 0)
                return content;

            if (!ModernDriverBaselineIsCurrentH20())
            {
                throw new InvalidOperationException(
                    "H20 driver baseline is unavailable or stale. Reload the save before writing.");
            }

            string output = content;
            foreach (string driverId in pending)
            {
                if (CountModernUnitHeadersH20(
                        output,
                        "driver_ai",
                        driverId) != 1)
                {
                    throw new InvalidDataException(
                        "H20 requires exactly one driver_ai block for " + driverId + ".");
                }

                ModernDriverSkillsSnapshotH20 baseline =
                    ModernDriverSkillBaselinesH20[driverId];
                ValidateModernDriverFieldsAgainstSnapshotH20(
                    output.Replace("\r\n", "\n")
                        .Replace("\r", "\n")
                        .Split('\n'),
                    driverId,
                    baseline,
                    "pre-write");

                Driver_AI current =
                    (object)SiiNunitData.SiiNitems[driverId] as Driver_AI;
                ValidateModernDriverSkillRangesH20(driverId, current);
                ModernDriverSkillsSnapshotH20 target =
                    CaptureModernDriverSkillsH20(current);

                output = PatchModernUnitFields(
                    output,
                    "driver_ai",
                    driverId,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "adr", target.Adr.ToString(CultureInfo.InvariantCulture) },
                        { "long_dist", target.LongDistance.ToString(CultureInfo.InvariantCulture) },
                        { "heavy", target.Heavy.ToString(CultureInfo.InvariantCulture) },
                        { "fragile", target.Fragile.ToString(CultureInfo.InvariantCulture) },
                        { "urgent", target.Urgent.ToString(CultureInfo.InvariantCulture) },
                        { "mechanical", target.Mechanical.ToString(CultureInfo.InvariantCulture) }
                    },
                    null);

                ValidateModernDriverFieldsAgainstSnapshotH20(
                    output.Replace("\r\n", "\n")
                        .Replace("\r", "\n")
                        .Split('\n'),
                    driverId,
                    target,
                    "post-write");

                IO_Utilities.LogWriter(
                    "RC5.1H20 driver skills validated: driver=" + driverId +
                    ", before={" + baseline.Describe() + "}" +
                    ", after={" + target.Describe() + "}" +
                    ", fields=6, block_count=1, result=allowed.");
            }

            return output;
        }

        private bool HasOtherPendingModernChangesForDriverH20(
            out string reason)
        {
            reason = "";

            if (HasPendingModernTrailerSwitchH14() ||
                !String.IsNullOrWhiteSpace(ModernPendingTruckSwitchTargetTruckId))
            {
                reason = "Existe uma troca de caminhão ou reboque preparada.";
                return true;
            }

            if (HasUnsavedModernVehicleChanges())
            {
                reason = "Existem alterações de veículo ou frota ainda não gravadas.";
                return true;
            }

            if (HasUnsavedModernGarageChangesH10() ||
                HasUnsavedModernHeadquartersChangesH12() ||
                HasUnsavedModernVisitedCityChangesH10() ||
                HasUnsavedModernMarketChangesH13R())
            {
                reason = "Existem alterações de garagem, sede, cidades ou mercados pendentes.";
                return true;
            }

            if (HasPendingModernAccessoryPackageH19())
            {
                reason = "Existe um pacote de peças preparado.";
                return true;
            }

            return false;
        }

        private void EnforceModernDriverMutationPolicyH20(
            object sender,
            EventArgs e)
        {
            if (!IsModernCompatibilityMode())
                return;

            buttonUserCompanyDriversHire.Enabled = false;
            buttonUserCompanyDriversFire.Enabled = false;
            contextMenuStripCompanyDriversHire.Enabled = false;
            contextMenuStripCompanyDriversFire.Enabled = false;
        }

        private void ModernCompanyDrivers_MouseDownH20(
            object sender,
            MouseEventArgs e)
        {
            if (!IsModernCompatibilityMode() ||
                e.Button != MouseButtons.Right)
                return;

            string pendingReason;
            bool blocked = HasOtherPendingModernChangesForDriverH20(
                out pendingReason);
            contextMenuStripCompanyDriversEdit.Enabled = !blocked;
            contextMenuStripCompanyDriversEdit.ToolTipText = blocked
                ? pendingReason
                : "H20: editar as seis habilidades do motorista com writer cirúrgico.";
            contextMenuStripCompanyDriversHire.Enabled = false;
            contextMenuStripCompanyDriversFire.Enabled = false;
            contextMenuStripCompanyDriversHire.ToolTipText =
                "H20: contratação permanece bloqueada até validar todas as referências do grafo.";
            contextMenuStripCompanyDriversFire.ToolTipText =
                "H20: demissão permanece bloqueada até validar todas as referências do grafo.";
        }

        private void ModernCompanyDriverEditCompletedH20(
            object sender,
            EventArgs e)
        {
            if (!IsModernCompatibilityMode())
                return;

            foreach (string driverId in GetPendingModernDriverSkillIdsH20())
            {
                Driver_AI current =
                    (object)SiiNunitData.SiiNitems[driverId] as Driver_AI;
                ValidateModernDriverSkillRangesH20(driverId, current);
                IO_Utilities.LogWriter(
                    "RC5.1H20 driver skills staged: driver=" + driverId +
                    ", values={" + CaptureModernDriverSkillsH20(current).Describe() +
                    "}, write_deferred=true.");
            }
        }

        private string[] GetModernFeatureMatrixLinesH20()
        {
            return new string[]
            {
                "TESTADO E VALIDADO | Euro Truck Simulator 2 1.60 | save 97; formatos 61-96 reconhecidos, sem homologação integral",
                "EXPERIMENTAL / NÃO VALIDADO | American Truck Simulator | sem ambiente ATS disponível; não é suporte oficial desta release",
                "HOMOLOGADO | Perfil, empresa, saldo, experiência, habilidades e cores | writer moderno cirúrgico",
                "HOMOLOGADO | Caminhões e reboques próprios | manutenção, placa, combustível, odômetro e operações em lote",
                "HOMOLOGADO | Troca de caminhão e reboque | topologias validadas e listas preservadas",
                "HOMOLOGADO | Garagens, sede e cidades visitadas | compra/evolução/redução/venda segura e Visit/Unvisit",
                "HOMOLOGADO | Mercados | Freight/Cargo com validação semântica",
                "HOMOLOGADO | Comboio | copiar/importar posição e rota sem salvamento automático",
                "HOMOLOGADO | Peças do caminhão | substituição verificada de data_path/refund no mesmo bloco",
                "HOMOLOGADO | Motoristas IA | edição segura das seis habilidades, sem contratar/demitir",
                "SOMENTE LEITURA | Catálogo de definições extraídas | índice de arquivos .sii sem liberar paths não verificados",
                "INDISPONÍVEL NA RELEASE SEGURA | Contratar/demitir motorista | mutação de grafo sem prova suficiente; bloqueio permanente nesta versão",
                "INDISPONÍVEL NA RELEASE SEGURA | Colar caminhão/reboque completo | recriação de grafos e IDs não é aceita no fluxo moderno",
                "INDISPONÍVEL NA RELEASE SEGURA | Editor bruto de veículo/reboque | inserção, exclusão e reordenação permanecem bloqueadas",
                "INDISPONÍVEL NA RELEASE SEGURA | Ferramentas avançadas legadas de comboio | somente o subconjunto H8 homologado foi mantido",
                "NÃO INCLUÍDO | Catálogo SCS nativo localizado | índice extraído permanece somente leitura; sem promessa universal para DLCs/mods",
                "CONTATO COMUNITÁRIO | Instagram | @fabio.meskita — " + CommunityInstagramUrlH21,
                "PUBLICAÇÃO COMUNITÁRIA | GitHub, issues, CI e releases | https://github.com/fabiomesquita01/ts-se-tool-community"
            };
        }

        private void ExportModernFeatureMatrixH20()
        {
            string path = Path.Combine(
                Application.StartupPath,
                "MATRIZ_RECURSOS_H22_FINAL.txt");
            File.WriteAllLines(
                path,
                GetModernFeatureMatrixLinesH20(),
                new UTF8Encoding(true));
            MessageBox.Show(
                "Matriz exportada para:\n" + path,
                "H22 — matriz final de recursos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private string ResolveExtractedDefRootH20(string selectedPath)
        {
            if (String.IsNullOrWhiteSpace(selectedPath) ||
                !Directory.Exists(selectedPath))
                return "";

            if (Path.GetFileName(selectedPath).Equals(
                    "def",
                    StringComparison.OrdinalIgnoreCase))
                return selectedPath;

            string child = Path.Combine(selectedPath, "def");
            if (Directory.Exists(child))
                return child;

            return selectedPath;
        }

        private void IndexExtractedDefinitionsH20()
        {
            using (FolderBrowserDialog picker = new FolderBrowserDialog())
            {
                picker.Description =
                    "Selecione uma pasta def já extraída do ETS2. " +
                    "A versão final apenas cria um índice de auditoria; não altera o save.";
                if (picker.ShowDialog(this) != DialogResult.OK)
                    return;

                string root = ResolveExtractedDefRootH20(picker.SelectedPath);
                List<string> files;
                try
                {
                    files = Directory.GetFiles(
                        root,
                        "*.sii",
                        SearchOption.AllDirectories).ToList();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Não foi possível ler a pasta selecionada:\n" + ex.Message,
                        "H21 — catálogo extraído",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                int truck = 0;
                int trailer = 0;
                int accessory = 0;
                List<string> relevant = new List<string>();
                foreach (string file in files)
                {
                    string relative = file.Substring(root.Length)
                        .TrimStart(Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    string lower = relative.ToLowerInvariant();
                    if (lower.Contains("vehicle/truck/"))
                        truck++;
                    if (lower.Contains("vehicle/trailer"))
                        trailer++;
                    if (lower.Contains("/accessory/"))
                        accessory++;
                    if (lower.Contains("vehicle/truck/") ||
                        lower.Contains("vehicle/trailer"))
                        relevant.Add(relative);
                }

                relevant.Sort(StringComparer.OrdinalIgnoreCase);
                string reportPath = Path.Combine(
                    Application.StartupPath,
                    "H21_CATALOGO_DEFS_EXTRAIDAS.txt");
                List<string> report = new List<string>();
                report.Add("TS SE Tool H21 — índice somente leitura de definições extraídas");
                report.Add("Pasta: " + root);
                report.Add("Arquivos .sii totais: " + files.Count.ToString(CultureInfo.InvariantCulture));
                report.Add("Arquivos de caminhão: " + truck.ToString(CultureInfo.InvariantCulture));
                report.Add("Arquivos de reboque: " + trailer.ToString(CultureInfo.InvariantCulture));
                report.Add("Caminhos em /accessory/: " + accessory.ToString(CultureInfo.InvariantCulture));
                report.Add("AVISO: este índice não habilita gravação. Refund, localização, DLC/mod e compatibilidade ainda precisam ser provados.");
                report.Add("");
                report.AddRange(relevant);
                File.WriteAllLines(reportPath, report, new UTF8Encoding(true));

                IO_Utilities.LogWriter(
                    "RC5.1H22 extracted-def catalog indexed: root=" + root +
                    ", sii=" + files.Count.ToString(CultureInfo.InvariantCulture) +
                    ", truck=" + truck.ToString(CultureInfo.InvariantCulture) +
                    ", trailer=" + trailer.ToString(CultureInfo.InvariantCulture) +
                    ", accessory=" + accessory.ToString(CultureInfo.InvariantCulture) +
                    ", write_enabled=false.");

                MessageBox.Show(
                    "Índice concluído em modo somente leitura.\n\n" +
                    "Arquivos .sii: " + files.Count.ToString(CultureInfo.CurrentCulture) +
                    "\nCaminhão: " + truck.ToString(CultureInfo.CurrentCulture) +
                    "\nReboque: " + trailer.ToString(CultureInfo.CurrentCulture) +
                    "\nAcessórios: " + accessory.ToString(CultureInfo.CurrentCulture) +
                    "\n\nRelatório: " + reportPath,
                    "H21 — catálogo extraído",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void ShowModernFeatureCenterH20()
        {
            using (Form dialog = new Form())
            {
                dialog.Text = "TS SE Tool — versão final comunitária";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MinimumSize = new System.Drawing.Size(820, 520);
                dialog.Size = new System.Drawing.Size(1040, 650);
                dialog.Icon = this.Icon;

                DataGridView grid = new DataGridView();
                grid.Dock = DockStyle.Fill;
                grid.ReadOnly = true;
                grid.AllowUserToAddRows = false;
                grid.AllowUserToDeleteRows = false;
                grid.AllowUserToResizeRows = false;
                grid.RowHeadersVisible = false;
                grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                grid.Columns.Add("Status", "Status");
                grid.Columns.Add("Recurso", "Recurso");
                grid.Columns.Add("Observacao", "Observação técnica");
                grid.Columns[0].FillWeight = 24F;
                grid.Columns[1].FillWeight = 34F;
                grid.Columns[2].FillWeight = 72F;

                foreach (string line in GetModernFeatureMatrixLinesH20())
                {
                    string[] parts = line.Split(new char[] { '|' }, 3);
                    if (parts.Length == 3)
                        grid.Rows.Add(parts[0].Trim(), parts[1].Trim(), parts[2].Trim());
                }

                Label notice = new Label();
                notice.Dock = DockStyle.Top;
                notice.Height = 48;
                notice.Padding = new Padding(8);
                notice.Text =
                    "A versão final prioriza segurança do save. Recursos sem writer moderno comprovado " +
                    "permanecem indisponíveis e nunca acionam o serializador legado.";

                FlowLayoutPanel actions = new FlowLayoutPanel();
                actions.Dock = DockStyle.Bottom;
                actions.Height = 48;
                actions.FlowDirection = FlowDirection.RightToLeft;
                actions.Padding = new Padding(6);

                Button close = new Button();
                close.Text = "Fechar";
                close.AutoSize = true;
                close.DialogResult = DialogResult.Cancel;

                Button export = new Button();
                export.Text = "Exportar matriz";
                export.AutoSize = true;
                export.Click += delegate { ExportModernFeatureMatrixH20(); };

                Button catalog = new Button();
                catalog.Text = "Indexar pasta def extraída";
                catalog.AutoSize = true;
                catalog.Click += delegate { IndexExtractedDefinitionsH20(); };

                Button github = new Button();
                github.Text = "Abrir GitHub";
                github.AutoSize = true;
                github.Click += delegate
                {
                    System.Diagnostics.Process.Start(
                        Utilities.Web_Utilities.External.linkGithub);
                };

                Button instagram = new Button();
                instagram.Text = "Abrir Instagram";
                instagram.AutoSize = true;
                instagram.Click += delegate
                {
                    System.Diagnostics.Process.Start(
                        CommunityInstagramUrlH21);
                };

                actions.Controls.Add(close);
                actions.Controls.Add(export);
                actions.Controls.Add(catalog);
                actions.Controls.Add(github);
                actions.Controls.Add(instagram);
                dialog.Controls.Add(grid);
                dialog.Controls.Add(notice);
                dialog.Controls.Add(actions);
                dialog.CancelButton = close;
                dialog.ShowDialog(this);
            }
        }

        private void EnsureModernFeatureCenterMenuH20()
        {
            if (ModernFeatureCenterMenuH20 != null)
                return;

            ModernFeatureCenterMenuH20 = new ToolStripMenuItem();
            ModernFeatureCenterMenuH20.Name = "toolStripMenuItemFeatureCenterH20";
            ModernFeatureCenterMenuH20.Text = "Central de recursos — versão final";
            ModernFeatureCenterMenuH20.ToolTipText =
                "Mostra recursos suportados, limites conhecidos e estado de validação.";
            ModernFeatureCenterMenuH20.Click += delegate
            {
                ShowModernFeatureCenterH20();
            };

            int separatorIndex = toolStripMenuItemProgram.DropDownItems.IndexOf(
                toolStripSeparator1);
            if (separatorIndex < 0)
                toolStripMenuItemProgram.DropDownItems.Add(
                    ModernFeatureCenterMenuH20);
            else
                toolStripMenuItemProgram.DropDownItems.Insert(
                    separatorIndex,
                    ModernFeatureCenterMenuH20);
        }

        private void InitializeModernFeatureCompletionH20()
        {
            CaptureModernDriverSkillBaselinesH20();
            EnsureModernFeatureCenterMenuH20();

            contextMenuStripCompanyDriversEdit.Enabled = true;
            contextMenuStripCompanyDriversHire.Enabled = false;
            contextMenuStripCompanyDriversFire.Enabled = false;
            buttonUserCompanyDriversHire.Enabled = false;
            buttonUserCompanyDriversFire.Enabled = false;

            toolTipMain.SetToolTip(
                listBoxUserCompanyDrivers,
                "H20: clique com o botão direito para editar habilidades. Contratar/demitir permanece bloqueado.");
            toolTipMain.SetToolTip(
                buttonUserCompanyDriversHire,
                "H20: contratação aguarda validação completa do grafo de referências.");
            toolTipMain.SetToolTip(
                buttonUserCompanyDriversFire,
                "H20: demissão aguarda validação completa do grafo de referências.");

            if (!ModernDriverEventsAttachedH20)
            {
                listBoxUserCompanyDrivers.MouseDown +=
                    ModernCompanyDrivers_MouseDownH20;
                listBoxUserCompanyDrivers.SelectedIndexChanged +=
                    EnforceModernDriverMutationPolicyH20;
                contextMenuStripCompanyDriversEdit.Click +=
                    ModernCompanyDriverEditCompletedH20;
                ModernDriverEventsAttachedH20 = true;
            }

            IO_Utilities.LogWriter(
                "RC5.1H22 release initialized: ets2_version=1.60, ets2_tested=true" +
                ", ets2_save_version_max=97, ats_validated=false" +
                ", driver_skill_editor=true, driver_hire_fire=false" +
                ", raw_vehicle_editor=false, extracted_def_catalog=read_only" +
                ", resource_matrix=true, compatibility_log_separated=true" +
                ", driver_baselines=" + ModernDriverSkillBaselinesH20.Count.ToString(
                    CultureInfo.InvariantCulture) + ".");
        }
    }
}
