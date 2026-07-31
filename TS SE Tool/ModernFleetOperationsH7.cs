using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

using TS_SE_Tool.Save.Items;
using TS_SE_Tool.Utilities;

namespace TS_SE_Tool
{
    public partial class FormMain
    {
        private GroupBox ModernFleetOperationsGroupH7;
        private Button ModernRepairAllTrucksButtonH7;
        private Button ModernRefuelAllTrucksButtonH7;
        private Button ModernRepairAllTrailersButtonH7;
        private Button ModernFullFleetServiceButtonH7;
        private Label ModernFleetOperationsStatusLabelH7;

        private void CreateModernFleetOperationsControlsH7()
        {
            if (ModernFleetOperationsGroupH7 != null ||
                tabPageCompany == null)
            {
                return;
            }

            ModernFleetOperationsGroupH7 = new GroupBox();
            ModernFleetOperationsGroupH7.Name =
                "groupBoxModernFleetOperationsH7";
            ModernFleetOperationsGroupH7.Text =
                "Operações da frota (H7)";
            ModernFleetOperationsGroupH7.Dock = DockStyle.Bottom;
            ModernFleetOperationsGroupH7.Height = 104;
            ModernFleetOperationsGroupH7.Padding =
                new Padding(8, 18, 8, 8);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Name = "tableLayoutModernFleetOperationsH7";
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 4;
            layout.RowCount = 2;
            layout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 25F));
            layout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 25F));
            layout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 25F));
            layout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 25F));
            layout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 65F));
            layout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 35F));

            ModernRepairAllTrucksButtonH7 =
                CreateModernFleetButtonH7(
                    "buttonModernRepairAllTrucksH7",
                    "Reparar caminhões");
            ModernRepairAllTrucksButtonH7.Click +=
                ModernRepairAllTrucksButtonH7_Click;

            ModernRefuelAllTrucksButtonH7 =
                CreateModernFleetButtonH7(
                    "buttonModernRefuelAllTrucksH7",
                    "Abastecer caminhões");
            ModernRefuelAllTrucksButtonH7.Click +=
                ModernRefuelAllTrucksButtonH7_Click;

            ModernRepairAllTrailersButtonH7 =
                CreateModernFleetButtonH7(
                    "buttonModernRepairAllTrailersH7",
                    "Reparar reboques");
            ModernRepairAllTrailersButtonH7.Click +=
                ModernRepairAllTrailersButtonH7_Click;

            ModernFullFleetServiceButtonH7 =
                CreateModernFleetButtonH7(
                    "buttonModernFullFleetServiceH7",
                    "Serviço completo");
            ModernFullFleetServiceButtonH7.Click +=
                ModernFullFleetServiceButtonH7_Click;

            layout.Controls.Add(
                ModernRepairAllTrucksButtonH7, 0, 0);
            layout.Controls.Add(
                ModernRefuelAllTrucksButtonH7, 1, 0);
            layout.Controls.Add(
                ModernRepairAllTrailersButtonH7, 2, 0);
            layout.Controls.Add(
                ModernFullFleetServiceButtonH7, 3, 0);

            ModernFleetOperationsStatusLabelH7 = new Label();
            ModernFleetOperationsStatusLabelH7.Name =
                "labelModernFleetOperationsStatusH7";
            ModernFleetOperationsStatusLabelH7.Dock =
                DockStyle.Fill;
            ModernFleetOperationsStatusLabelH7.TextAlign =
                System.Drawing.ContentAlignment.MiddleLeft;
            ModernFleetOperationsStatusLabelH7.AutoEllipsis = true;
            ModernFleetOperationsStatusLabelH7.Text =
                "As operações são preparadas em memória; clique em Salvar para gravar.";

            layout.Controls.Add(
                ModernFleetOperationsStatusLabelH7, 0, 1);
            layout.SetColumnSpan(
                ModernFleetOperationsStatusLabelH7, 4);

            ModernFleetOperationsGroupH7.Controls.Add(layout);
            tabPageCompany.Controls.Add(
                ModernFleetOperationsGroupH7);
            ModernFleetOperationsGroupH7.BringToFront();
        }

        private Button CreateModernFleetButtonH7(
            string name,
            string text)
        {
            Button button = new Button();
            button.Name = name;
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(3);
            button.UseVisualStyleBackColor = true;
            return button;
        }

        private bool CanRunModernFleetOperationH7(
            out string reason)
        {
            reason = "";

            if (!IsModernCompatibilityMode())
            {
                reason = "A operação H7 está disponível apenas no modo moderno.";
                return false;
            }

            if (!ModernFleetBaselineIsCurrentH6())
            {
                reason =
                    "A base da frota está ausente ou desatualizada. Recarregue o save.";
                return false;
            }

            if (!String.IsNullOrWhiteSpace(
                    ModernPendingTruckSwitchTargetTruckId))
            {
                reason =
                    "Conclua ou descarte a troca de caminhão preparada antes da operação em lote.";
                return false;
            }

            return true;
        }

        private bool RepairModernTruckH7(Vehicle truck)
        {
            if (truck == null)
                return false;

            bool changed = false;

            if ((float)truck.engine_wear != 0F)
            {
                truck.engine_wear = 0;
                changed = true;
            }
            if ((float)truck.transmission_wear != 0F)
            {
                truck.transmission_wear = 0;
                changed = true;
            }
            if ((float)truck.chassis_wear != 0F)
            {
                truck.chassis_wear = 0;
                changed = true;
            }
            if ((float)truck.cabin_wear != 0F)
            {
                truck.cabin_wear = 0;
                changed = true;
            }

            if (truck.wheels_wear != null)
            {
                for (int index = 0;
                     index < truck.wheels_wear.Count;
                     index++)
                {
                    if ((float)truck.wheels_wear[index] != 0F)
                    {
                        truck.wheels_wear[index] = 0;
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private bool RefuelModernTruckH7(Vehicle truck)
        {
            if (truck == null ||
                (float)truck.fuel_relative == 1F)
            {
                return false;
            }

            truck.fuel_relative = 1;
            return true;
        }

        private bool RepairModernTrailerH7(Trailer trailer)
        {
            if (trailer == null)
                return false;

            bool changed = false;

            if ((float)trailer.cargo_damage != 0F)
            {
                trailer.cargo_damage = 0;
                changed = true;
            }
            if ((float)trailer.trailer_body_wear != 0F)
            {
                trailer.trailer_body_wear = 0;
                changed = true;
            }
            if ((float)trailer.chassis_wear != 0F)
            {
                trailer.chassis_wear = 0;
                changed = true;
            }

            if (trailer.wheels_wear != null)
            {
                for (int index = 0;
                     index < trailer.wheels_wear.Count;
                     index++)
                {
                    if ((float)trailer.wheels_wear[index] != 0F)
                    {
                        trailer.wheels_wear[index] = 0;
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private void RunModernFleetOperationH7(
            bool repairTrucks,
            bool refuelTrucks,
            bool repairTrailers,
            string operationName)
        {
            string reason;
            if (!CanRunModernFleetOperationH7(out reason))
            {
                MessageBox.Show(
                    reason,
                    "H7 — operação bloqueada",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                operationName +
                " será aplicada a todos os veículos próprios compatíveis.\r\n\r\n" +
                "Nada será gravado até você clicar em Salvar. Continuar?",
                "H7 — confirmar operação da frota",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirmation != DialogResult.Yes)
                return;

            int changedTrucks = 0;
            int changedTrailers = 0;

            foreach (string truckId in
                     new List<string>(ModernOwnedTruckBaselinesH6.Keys))
            {
                if (!IsOwnedModernTruckH6(truckId) ||
                    !IsModernTruckObject(truckId))
                {
                    continue;
                }

                Vehicle truck =
                    (Vehicle)SiiNunitData.SiiNitems[truckId];
                bool truckChanged = false;

                if (repairTrucks)
                    truckChanged |= RepairModernTruckH7(truck);
                if (refuelTrucks)
                    truckChanged |= RefuelModernTruckH7(truck);

                if (truckChanged)
                    changedTrucks++;
            }

            if (repairTrailers)
            {
                foreach (string trailerId in
                         new List<string>(ModernOwnedTrailerBaselinesH6.Keys))
                {
                    if (!IsOwnedModernTrailerH6(trailerId) ||
                        !IsModernTrailerObject(trailerId))
                    {
                        continue;
                    }

                    Trailer trailer =
                        (Trailer)SiiNunitData.SiiNitems[trailerId];

                    if (RepairModernTrailerH7(trailer))
                        changedTrailers++;
                }
            }

            UpdateModernTruckFleetEditUiH6();
            UpdateModernTrailerFleetEditUiH6();
            UpdateModernFleetOperationsStateH7();

            string result =
                "Caminhões alterados: " +
                changedTrucks.ToString(CultureInfo.InvariantCulture) +
                "\r\nReboques alterados: " +
                changedTrailers.ToString(CultureInfo.InvariantCulture);

            if (changedTrucks == 0 && changedTrailers == 0)
            {
                MessageBox.Show(
                    result +
                    "\r\n\r\nA frota já estava no estado solicitado.",
                    "H7 — nenhuma alteração necessária",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            IO_Utilities.LogWriter(
                "RC5.1H7 fleet operation prepared: " +
                operationName +
                ", trucks=" +
                changedTrucks.ToString(CultureInfo.InvariantCulture) +
                ", trailers=" +
                changedTrailers.ToString(CultureInfo.InvariantCulture));

            MessageBox.Show(
                result +
                "\r\n\r\nClique em Salvar para gravar as alterações.",
                "H7 — operação preparada",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ModernRepairAllTrucksButtonH7_Click(
            object sender,
            EventArgs e)
        {
            RunModernFleetOperationH7(
                true,
                false,
                false,
                "Reparo de todos os caminhões");
        }

        private void ModernRefuelAllTrucksButtonH7_Click(
            object sender,
            EventArgs e)
        {
            RunModernFleetOperationH7(
                false,
                true,
                false,
                "Abastecimento de todos os caminhões");
        }

        private void ModernRepairAllTrailersButtonH7_Click(
            object sender,
            EventArgs e)
        {
            RunModernFleetOperationH7(
                false,
                false,
                true,
                "Reparo de todos os reboques");
        }

        private void ModernFullFleetServiceButtonH7_Click(
            object sender,
            EventArgs e)
        {
            RunModernFleetOperationH7(
                true,
                true,
                true,
                "Serviço completo da frota");
        }

        private void UpdateModernFleetOperationsStateH7()
        {
            if (ModernFleetOperationsGroupH7 == null)
                return;

            string reason;
            bool enabled =
                CanRunModernFleetOperationH7(out reason);

            ModernFleetOperationsGroupH7.Enabled = true;

            foreach (Button button in new Button[]
            {
                ModernRepairAllTrucksButtonH7,
                ModernRefuelAllTrucksButtonH7,
                ModernRepairAllTrailersButtonH7,
                ModernFullFleetServiceButtonH7
            })
            {
                if (button != null)
                    button.Enabled = enabled;
            }

            if (ModernFleetOperationsStatusLabelH7 != null)
            {
                if (enabled)
                {
                    ModernFleetOperationsStatusLabelH7.Text =
                        ModernOwnedTruckBaselinesH6.Count.ToString(
                            CultureInfo.InvariantCulture) +
                        " caminhões e " +
                        ModernOwnedTrailerBaselinesH6.Count.ToString(
                            CultureInfo.InvariantCulture) +
                        " reboques próprios disponíveis. Clique em Salvar após preparar a operação.";
                }
                else
                {
                    ModernFleetOperationsStatusLabelH7.Text = reason;
                }
            }
        }

        private void InitializeModernFleetOperationsH7()
        {
            if (!IsModernCompatibilityMode())
                return;

            CreateModernFleetOperationsControlsH7();
            UpdateModernFleetOperationsStateH7();

            IO_Utilities.LogWriter(
                "RC5.1H7 batch fleet operations initialized.");
        }
    }
}
