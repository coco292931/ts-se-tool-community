using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using TS_SE_Tool.Utilities;

namespace TS_SE_Tool
{
    public partial class FormMain
    {
        private bool ModernGarageEventsAttachedH10;
        private bool ModernGarageUiRefreshingH10;
        private bool ModernGarageChangesPendingH10;
        private readonly HashSet<string> ModernGarageChangedNamesH101 =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);


        private bool ModernHeadquartersUiRefreshingH12;
        private bool ModernHeadquartersChangesPendingH12;
        private string ModernHeadquartersBaselineH12 = "";

        private string NormalizeModernCityIdH12(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "";

            return value.Trim().Trim('"');
        }

        private bool HasUnsavedModernHeadquartersChangesH12()
        {
            if (!ModernHeadquartersChangesPendingH12 ||
                SiiNunitData == null ||
                SiiNunitData.Player == null)
            {
                return false;
            }

            return !String.Equals(
                NormalizeModernCityIdH12(
                    SiiNunitData.Player.hq_city),
                NormalizeModernCityIdH12(
                    ModernHeadquartersBaselineH12),
                StringComparison.OrdinalIgnoreCase);
        }

        private Garages FindModernGarageByCityH12(
            string cityId)
        {
            string normalized = NormalizeModernCityIdH12(cityId);
            if (String.IsNullOrWhiteSpace(normalized) ||
                GaragesList == null)
            {
                return null;
            }

            return GaragesList.Find(
                garage => garage != null &&
                String.Equals(
                    NormalizeModernCityIdH12(
                        garage.GarageName),
                    normalized,
                    StringComparison.OrdinalIgnoreCase));
        }

        private string GetModernGarageDisplayNameH12(
            string cityId)
        {
            Garages garage = FindModernGarageByCityH12(cityId);
            if (garage == null ||
                String.IsNullOrWhiteSpace(
                    garage.GarageNameTranslated))
            {
                return NormalizeModernCityIdH12(cityId);
            }

            return garage.GarageNameTranslated;
        }

        private bool HasUnsavedModernGarageChangesH10()
        {
            return ModernGarageChangesPendingH10 ||
                   ModernGarageChangedNamesH101.Count > 0;
        }

        private bool HasUnsavedModernVisitedCityChangesH10()
        {
            if (SiiNunitData == null ||
                SiiNunitData.Economy == null ||
                CitiesList == null)
            {
                return false;
            }

            HashSet<string> saved =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string city in SiiNunitData.Economy.visited_cities)
            {
                string id = (city ?? "").Trim().Trim('"');
                if (!String.IsNullOrWhiteSpace(id))
                    saved.Add(id);
            }

            HashSet<string> current =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (City city in CitiesList)
            {
                if (city == null || !city.Visited)
                    continue;

                string id = (city.CityName ?? "").Trim().Trim('"');
                if (!String.IsNullOrWhiteSpace(id))
                    current.Add(id);
            }

            return !saved.SetEquals(current);
        }


        private bool CanModifyModernGaragesH10(out string reason)
        {
            reason = "";

            if (!IsModernCompatibilityMode())
            {
                reason = "A função de garagens é exclusiva do modo de compatibilidade moderno.";
                return false;
            }

            if (SiiNunitData == null ||
                SiiNunitData.Economy == null ||
                SiiNunitData.Player == null ||
                GaragesList == null)
            {
                reason = "Os dados do save ainda não estão carregados.";
                return false;
            }

            if (!String.IsNullOrWhiteSpace(
                    NormalizeModernReference(
                        SiiNunitData.Player.current_job)))
            {
                reason = "Finalize ou cancele a entrega ativa antes de editar garagens.";
                return false;
            }

            if (!String.IsNullOrWhiteSpace(
                    ModernPendingTruckSwitchTargetTruckId))
            {
                reason = "Salve ou descarte a troca de caminhão pendente antes de editar garagens.";
                return false;
            }

            if (HasUnsavedModernFleetChangesH6())
            {
                reason = "Salve ou descarte a manutenção da frota antes de editar garagens.";
                return false;
            }

            if (ModernConvoyStatusLabelH8 != null &&
                ModernConvoyStatusLabelH8.Text != null &&
                ModernConvoyStatusLabelH8.Text.IndexOf(
                    "em memória",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reason = "Salve ou descarte a importação de posição/rota H8 antes de editar garagens.";
                return false;
            }

            if (HasUnsavedModernVisitedCityChangesH10())
            {
                reason = "Salve ou descarte as alterações de cidades visitadas antes de editar garagens.";
                return false;
            }

            if (HasUnsavedModernHeadquartersChangesH12())
            {
                reason = "Salve ou descarte a alteração da cidade-sede antes de editar garagens.";
                return false;
            }

            return true;
        }


        private List<Garages> GetSelectedModernGaragesH10()
        {
            List<Garages> garages = new List<Garages>();

            if (listBoxGarages.SelectedItems.Count == 0)
            {
                foreach (object item in listBoxGarages.Items)
                {
                    Garages garage = item as Garages;
                    if (garage != null)
                        garages.Add(garage);
                }
            }
            else
            {
                foreach (object item in listBoxGarages.SelectedItems)
                {
                    Garages garage = item as Garages;
                    if (garage != null)
                        garages.Add(garage);
                }
            }

            return garages;
        }

        private int GetGarageCapacityH10(int status)
        {
            if (status == 6)
                return 1;
            if (status == 2)
                return 3;
            if (status == 3)
                return 5;
            return 0;
        }

        private bool HasOccupiedGarageSlotsAboveH10(
            List<string> slots,
            int capacity)
        {
            if (slots == null)
                return false;

            for (int index = Math.Max(0, capacity);
                 index < slots.Count;
                 index++)
            {
                if (!String.IsNullOrWhiteSpace(
                        NormalizeModernReference(slots[index])))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanSafelyDowngradeGarageH10(
            Garages garage,
            out int targetStatus,
            out string reason)
        {
            targetStatus = garage != null ? garage.GarageStatus : 0;
            reason = "";

            if (garage == null)
            {
                reason = "Garagem inválida.";
                return false;
            }

            if (garage.GarageStatus == 3)
            {
                targetStatus = 2;
            }
            else if (garage.GarageStatus == 2 &&
                     String.Equals(
                         garage.GarageName,
                         SiiNunitData.Player.hq_city,
                         StringComparison.OrdinalIgnoreCase))
            {
                targetStatus = 6;
            }
            else
            {
                reason = "A garagem não possui um nível inferior compatível.";
                return false;
            }

            int targetCapacity = GetGarageCapacityH10(targetStatus);
            if (HasOccupiedGarageSlotsAboveH10(
                    garage.Vehicles,
                    targetCapacity) ||
                HasOccupiedGarageSlotsAboveH10(
                    garage.Drivers,
                    targetCapacity))
            {
                reason = "A garagem possui caminhão ou motorista em uma vaga que seria removida.";
                return false;
            }

            return true;
        }

        private bool ConfirmModernGarageOperationH10(
            string operation,
            int eligible,
            int selected,
            bool allGarages)
        {
            if (eligible <= 0)
            {
                MessageBox.Show(
                    "Nenhuma garagem selecionada está apta para esta operação.",
                    "TS SE Tool H12",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            string scope = allGarages
                ? "Nenhuma garagem está selecionada. O comando verificará TODAS as " + selected + " garagens exibidas."
                : selected + " garagem(ns) selecionada(s) serão verificadas.";

            DialogResult result = MessageBox.Show(
                scope + "\r\n\r\n" +
                operation + " em " + eligible + " garagem(ns) apta(s)?" +
                "\r\n\r\nA alteração será preparada em memória e somente será gravada ao clicar em Salvar.",
                "Confirmar operação de garagem H12",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            return result == DialogResult.Yes;
        }

        private void ResizeModernGarageSlotsH101(
            List<string> slots,
            int capacity,
            string description)
        {
            if (slots == null)
                throw new InvalidOperationException(
                    "A lista de " + description + " da garagem não está disponível.");

            while (slots.Count > capacity)
            {
                int last = slots.Count - 1;
                if (!String.IsNullOrWhiteSpace(
                        NormalizeModernReference(slots[last])))
                {
                    throw new InvalidOperationException(
                        "A redução tentaria remover uma vaga ocupada de " + description + ".");
                }

                slots.RemoveAt(last);
            }

            while (slots.Count < capacity)
                slots.Add(null);
        }

        private Save.Items.Garage GetModernGarageBlockH101(
            Garages garage)
        {
            if (garage == null)
                throw new InvalidOperationException("Garagem inválida.");

            string unitId = "garage." + garage.GarageName;
            if (!SiiNunitData.SiiNitems.ContainsKey(unitId))
            {
                throw new InvalidOperationException(
                    "O bloco moderno da garagem não foi encontrado: " + unitId);
            }

            Save.Items.Garage block =
                SiiNunitData.SiiNitems[unitId] as Save.Items.Garage;
            if (block == null)
            {
                throw new InvalidOperationException(
                    "O bloco localizado não é uma garagem válida: " + unitId);
            }

            return block;
        }

        private void ApplyModernGarageStatusH101(
            Garages garage,
            int targetStatus)
        {
            int capacity = GetGarageCapacityH10(targetStatus);
            if (capacity <= 0)
                throw new InvalidOperationException(
                    "Status de garagem não permitido na H10.1: " +
                    targetStatus.ToString(CultureInfo.InvariantCulture));

            ResizeModernGarageSlotsH101(
                garage.Vehicles,
                capacity,
                "caminhões");
            ResizeModernGarageSlotsH101(
                garage.Drivers,
                capacity,
                "motoristas");

            garage.GarageStatus = targetStatus;

            Save.Items.Garage block = GetModernGarageBlockH101(garage);
            block.status = targetStatus;
            block.vehicles = new List<string>(garage.Vehicles);
            block.drivers = new List<string>(garage.Drivers);

            ModernGarageChangedNamesH101.Add(garage.GarageName);
        }

        private void MarkModernGarageChangesH10(
            string operation,
            int affected)
        {
            ModernGarageChangesPendingH10 = true;

            try
            {
                toolStripStatusMessages.ForeColor =
                    System.Drawing.Color.Black;
                toolStripStatusMessages.Text =
                    operation + " preparada em " + affected +
                    " garagem(ns). Clique em Salvar.";
            }
            catch (Exception ex)
            {
                IO_Utilities.ErrorLogWriter(
                    "RC5.1H12 ignorou uma falha ao atualizar a barra de status: " +
                    ex.Message);
            }

            MessageBox.Show(
                operation + " preparada em " + affected + " garagem(ns).\r\n\r\nClique em Salvar para gravar no TESTE_TSSET.",
                "TS SE Tool H12",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private bool HasModernGarageAssignmentsH11(
            List<string> references)
        {
            if (references == null)
                return false;

            foreach (string reference in references)
            {
                if (!String.IsNullOrWhiteSpace(
                        NormalizeModernReference(reference)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanSafelySellGarageH11(
            Garages garage,
            out string reason)
        {
            reason = "";

            if (garage == null)
            {
                reason = "Garagem inválida.";
                return false;
            }

            if (garage.GarageStatus == 0)
            {
                reason = "A garagem já não pertence à empresa.";
                return false;
            }

            if (String.Equals(
                    garage.GarageName,
                    SiiNunitData.Player.hq_city,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "A garagem-sede não pode ser vendida.";
                return false;
            }

            if (garage.GarageStatus != 2 &&
                garage.GarageStatus != 3)
            {
                reason =
                    "A garagem possui um status que não pode ser vendido com segurança.";
                return false;
            }

            if (HasModernGarageAssignmentsH11(garage.Vehicles))
            {
                reason = "A garagem ainda contém caminhão atribuído.";
                return false;
            }

            if (HasModernGarageAssignmentsH11(garage.Drivers))
            {
                reason = "A garagem ainda contém motorista atribuído.";
                return false;
            }

            if (HasModernGarageAssignmentsH11(garage.Trailers))
            {
                reason = "A garagem ainda contém reboque atribuído.";
                return false;
            }

            return true;
        }

        private void ApplyModernGarageSaleH11(
            Garages garage)
        {
            string reason;
            if (!CanSafelySellGarageH11(garage, out reason))
            {
                throw new InvalidOperationException(
                    "A garagem não pode ser vendida: " + reason);
            }

            garage.Vehicles.Clear();
            garage.Drivers.Clear();
            garage.GarageStatus = 0;

            Save.Items.Garage block =
                GetModernGarageBlockH101(garage);
            block.status = 0;
            block.vehicles = new List<string>();
            block.drivers = new List<string>();

            ModernGarageChangedNamesH101.Add(
                garage.GarageName);
        }

        private void ModernSellGaragesH11(
            object sender,
            EventArgs e)
        {
            string reason;
            if (!CanModifyModernGaragesH10(out reason))
            {
                MessageBox.Show(
                    reason,
                    "TS SE Tool H12",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (listBoxGarages.SelectedItems.Count == 0)
            {
                MessageBox.Show(
                    "Selecione explicitamente uma ou mais garagens vazias para vender.\r\n\r\nA H11 não executa venda em massa sem seleção.",
                    "Venda segura de garagem H11",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            List<Garages> selected =
                GetSelectedModernGaragesH10();
            List<Garages> eligible =
                new List<Garages>();

            foreach (Garages garage in selected)
            {
                string garageReason;
                if (!CanSafelySellGarageH11(
                        garage,
                        out garageReason))
                {
                    MessageBox.Show(
                        "A garagem " +
                        garage.GarageNameTranslated +
                        " não pode ser vendida: " +
                        garageReason,
                        "Venda de garagem bloqueada",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                eligible.Add(garage);
            }

            if (!ConfirmModernGarageOperationH10(
                    "Vender",
                    eligible.Count,
                    selected.Count,
                    false))
            {
                return;
            }

            int topIndex = listBoxGarages.TopIndex;
            foreach (Garages garage in eligible)
                ApplyModernGarageSaleH11(garage);

            FillGaragesList(topIndex);
            RefreshModernHeadquartersChoicesH12();
            MarkModernGarageChangesH10(
                "Venda de garagem",
                eligible.Count);
        }

        private bool CanModifyModernHeadquartersH12(
            out string reason)
        {
            reason = "";

            if (!IsModernCompatibilityMode())
            {
                reason =
                    "A alteração da cidade-sede é exclusiva do modo de compatibilidade moderno.";
                return false;
            }

            if (SiiNunitData == null ||
                SiiNunitData.Economy == null ||
                SiiNunitData.Player == null ||
                GaragesList == null)
            {
                reason = "Os dados do save ainda não estão carregados.";
                return false;
            }

            if (!String.IsNullOrWhiteSpace(
                    NormalizeModernReference(
                        SiiNunitData.Player.current_job)))
            {
                reason =
                    "Finalize ou cancele a entrega ativa antes de alterar a cidade-sede.";
                return false;
            }

            if (!String.IsNullOrWhiteSpace(
                    ModernPendingTruckSwitchTargetTruckId))
            {
                reason =
                    "Salve ou descarte a troca de caminhão pendente antes de alterar a cidade-sede.";
                return false;
            }

            if (HasUnsavedModernFleetChangesH6())
            {
                reason =
                    "Salve ou descarte a manutenção da frota antes de alterar a cidade-sede.";
                return false;
            }

            if (ModernConvoyStatusLabelH8 != null &&
                ModernConvoyStatusLabelH8.Text != null &&
                ModernConvoyStatusLabelH8.Text.IndexOf(
                    "em memória",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reason =
                    "Salve ou descarte a importação de posição/rota H8 antes de alterar a cidade-sede.";
                return false;
            }

            if (HasUnsavedModernVisitedCityChangesH10())
            {
                reason =
                    "Salve ou descarte as alterações de cidades visitadas antes de alterar a cidade-sede.";
                return false;
            }

            if (HasUnsavedModernGarageChangesH10())
            {
                reason =
                    "Salve ou descarte as alterações de garagem antes de alterar a cidade-sede.";
                return false;
            }

            return true;
        }

        private void AttachModernHeadquartersEventH12()
        {
            if (comboBoxUserCompanyHQcity == null)
                return;

            // SelectedIndexChanged tambem dispara durante DataSource/SelectedValue.
            // SelectionChangeCommitted ocorre somente quando o usuario escolhe uma opcao.
            comboBoxUserCompanyHQcity.SelectedIndexChanged -=
                comboBoxUserCompanyHQcity_SelectedIndexChanged;
            comboBoxUserCompanyHQcity.SelectedIndexChanged -=
                ModernHeadquartersSelectionChangedH12;
            comboBoxUserCompanyHQcity.SelectionChangeCommitted -=
                ModernHeadquartersSelectionChangedH12;
            comboBoxUserCompanyHQcity.SelectionChangeCommitted +=
                ModernHeadquartersSelectionChangedH12;
        }

        private void RestoreModernHeadquartersSelectionH12(
            string cityId)
        {
            if (comboBoxUserCompanyHQcity == null)
                return;

            ModernHeadquartersUiRefreshingH12 = true;
            try
            {
                comboBoxUserCompanyHQcity.SelectedValue =
                    NormalizeModernCityIdH12(cityId);
            }
            finally
            {
                ModernHeadquartersUiRefreshingH12 = false;
            }
        }

        private void RefreshModernHeadquartersChoicesH12()
        {
            if (!IsModernCompatibilityMode() ||
                comboBoxUserCompanyHQcity == null ||
                SiiNunitData == null ||
                SiiNunitData.Player == null)
            {
                return;
            }

            string current =
                NormalizeModernCityIdH12(
                    SiiNunitData.Player.hq_city);

            ModernHeadquartersUiRefreshingH12 = true;
            try
            {
                FillHQcities();
                comboBoxUserCompanyHQcity.DropDownStyle =
                    ComboBoxStyle.DropDownList;
                comboBoxUserCompanyHQcity.Enabled = true;
                comboBoxUserCompanyHQcity.TabStop = true;
                comboBoxUserCompanyHQcity.SelectedValue =
                    current;
            }
            finally
            {
                ModernHeadquartersUiRefreshingH12 = false;
            }

            AttachModernHeadquartersEventH12();
        }

        private void ModernHeadquartersSelectionChangedH12(
            object sender,
            EventArgs e)
        {
            if (ModernHeadquartersUiRefreshingH12 ||
                !IsModernCompatibilityMode() ||
                comboBoxUserCompanyHQcity == null ||
                SiiNunitData == null ||
                SiiNunitData.Player == null)
            {
                return;
            }

            string previous =
                NormalizeModernCityIdH12(
                    SiiNunitData.Player.hq_city);
            string selected =
                NormalizeModernCityIdH12(
                    comboBoxUserCompanyHQcity.SelectedValue == null
                        ? ""
                        : comboBoxUserCompanyHQcity.SelectedValue.ToString());

            if (String.IsNullOrWhiteSpace(selected))
            {
                RestoreModernHeadquartersSelectionH12(previous);
                return;
            }

            if (String.Equals(
                    selected,
                    previous,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string reason;
            if (!CanModifyModernHeadquartersH12(out reason))
            {
                RestoreModernHeadquartersSelectionH12(previous);
                MessageBox.Show(
                    reason,
                    "Alteração da cidade-sede bloqueada",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Garages destination =
                FindModernGarageByCityH12(selected);
            if (destination == null ||
                destination.IgnoreStatus ||
                destination.GarageStatus == 0)
            {
                RestoreModernHeadquartersSelectionH12(previous);
                MessageBox.Show(
                    "A nova cidade-sede precisa possuir uma garagem adquirida e reconhecida pelo save.",
                    "Cidade-sede inválida",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string previousName =
                GetModernGarageDisplayNameH12(previous);
            string selectedName =
                GetModernGarageDisplayNameH12(selected);

            DialogResult confirmation = MessageBox.Show(
                "Alterar a cidade-sede de " +
                previousName +
                " para " +
                selectedName +
                "?\r\n\r\nA alteração ficará em memória e somente será gravada ao clicar em Salvar.",
                "Confirmar cidade-sede H12",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (confirmation != DialogResult.Yes)
            {
                RestoreModernHeadquartersSelectionH12(previous);
                return;
            }

            SiiNunitData.Player.hq_city = selected;
            ModernHeadquartersChangesPendingH12 =
                !String.Equals(
                    selected,
                    NormalizeModernCityIdH12(
                        ModernHeadquartersBaselineH12),
                    StringComparison.OrdinalIgnoreCase);

            int topIndex = listBoxGarages.TopIndex;
            FillGaragesList(topIndex);

            try
            {
                toolStripStatusMessages.ForeColor =
                    System.Drawing.Color.Black;
                toolStripStatusMessages.Text =
                    "Cidade-sede alterada para " +
                    selectedName +
                    ". Clique em Salvar.";
            }
            catch (Exception ex)
            {
                IO_Utilities.ErrorLogWriter(
                    "RC5.1H12 ignorou uma falha ao atualizar a barra de status: " +
                    ex.Message);
            }

            MessageBox.Show(
                "Cidade-sede alterada para " +
                selectedName +
                ".\r\n\r\nClique em Salvar para gravar no TESTE_TSSET.",
                "TS SE Tool H12",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private bool TryEnterModernPlayerBlockH12(
            string trimmed)
        {
            return !String.IsNullOrWhiteSpace(trimmed) &&
                   trimmed.StartsWith(
                       "player :",
                       StringComparison.Ordinal) &&
                   trimmed.EndsWith(
                       "{",
                       StringComparison.Ordinal);
        }

        private string PatchModernHeadquartersDataH12(
            string input)
        {
            if (String.IsNullOrWhiteSpace(input) ||
                !HasUnsavedModernHeadquartersChangesH12())
            {
                return input;
            }

            string target =
                NormalizeModernCityIdH12(
                    SiiNunitData.Player.hq_city);
            Garages destination =
                FindModernGarageByCityH12(target);
            if (destination == null ||
                destination.IgnoreStatus ||
                destination.GarageStatus == 0)
            {
                throw new InvalidOperationException(
                    "A cidade-sede selecionada não possui uma garagem adquirida válida: " +
                    target);
            }

            string normalized =
                input.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = normalized.Split('\n');
            List<string> output =
                new List<string>(lines.Length);

            bool inPlayer = false;
            bool sawPlayer = false;
            bool sawHeadquarters = false;

            foreach (string originalLine in lines)
            {
                string line = originalLine ?? "";
                string trimmed = line.Trim();

                if (!inPlayer)
                {
                    if (TryEnterModernPlayerBlockH12(trimmed))
                    {
                        if (sawPlayer)
                        {
                            throw new InvalidOperationException(
                                "Mais de um bloco player foi localizado durante a gravação da cidade-sede.");
                        }

                        inPlayer = true;
                        sawPlayer = true;
                    }

                    output.Add(line);
                    continue;
                }

                if (trimmed == "}")
                {
                    if (!sawHeadquarters)
                    {
                        throw new InvalidOperationException(
                            "O campo hq_city não foi localizado dentro do bloco player.");
                    }

                    inPlayer = false;
                    output.Add(line);
                    continue;
                }

                int colonPosition = trimmed.IndexOf(':');
                if (colonPosition >= 0)
                {
                    string tag =
                        trimmed.Substring(
                            0,
                            colonPosition).Trim();

                    if (tag == "hq_city")
                    {
                        if (sawHeadquarters)
                        {
                            throw new InvalidOperationException(
                                "O campo hq_city apareceu mais de uma vez no bloco player.");
                        }

                        output.Add(
                            GetLeadingWhitespaceH101(line) +
                            "hq_city: " +
                            target);
                        sawHeadquarters = true;
                        continue;
                    }
                }

                output.Add(line);
            }

            if (inPlayer)
            {
                throw new InvalidOperationException(
                    "O bloco player não foi encerrado durante a gravação da cidade-sede.");
            }

            if (!sawPlayer || !sawHeadquarters)
            {
                throw new InvalidOperationException(
                    "Não foi possível atualizar cirurgicamente o campo hq_city.");
            }

            IO_Utilities.LogWriter(
                "RC5.1H12 patched player.hq_city surgically: " +
                target);

            return String.Join(
                Environment.NewLine,
                output);
        }

        private void ModernBuyGaragesH10(object sender, EventArgs e)
        {
            string reason;
            if (!CanModifyModernGaragesH10(out reason))
            {
                MessageBox.Show(
                    reason,
                    "TS SE Tool H12",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            List<Garages> selected = GetSelectedModernGaragesH10();
            List<Garages> eligible = new List<Garages>();
            foreach (Garages garage in selected)
            {
                if (garage.GarageStatus != 0)
                    continue;

                if (HasOccupiedGarageSlotsAboveH10(garage.Vehicles, 0) ||
                    HasOccupiedGarageSlotsAboveH10(garage.Drivers, 0) ||
                    (garage.Trailers != null && garage.Trailers.Count > 0))
                {
                    MessageBox.Show(
                        "A garagem " + garage.GarageNameTranslated +
                        " está marcada como não adquirida, mas contém veículos, motoristas ou reboques. A operação foi bloqueada.",
                        "Topologia de garagem incompatível",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                eligible.Add(garage);
            }

            bool allGarages = listBoxGarages.SelectedItems.Count == 0;
            if (!ConfirmModernGarageOperationH10(
                    "Comprar",
                    eligible.Count,
                    selected.Count,
                    allGarages))
            {
                return;
            }

            int topIndex = listBoxGarages.TopIndex;
            foreach (Garages garage in eligible)
                ApplyModernGarageStatusH101(garage, 2);

            FillGaragesList(topIndex);
            RefreshModernHeadquartersChoicesH12();
            MarkModernGarageChangesH10("Compra de garagem", eligible.Count);
        }

        private void ModernUpgradeGaragesH10(object sender, EventArgs e)
        {
            string reason;
            if (!CanModifyModernGaragesH10(out reason))
            {
                MessageBox.Show(
                    reason,
                    "TS SE Tool H12",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            List<Garages> selected = GetSelectedModernGaragesH10();
            List<Garages> eligible = new List<Garages>();
            foreach (Garages garage in selected)
            {
                if (garage.GarageStatus == 2 ||
                    garage.GarageStatus == 6)
                {
                    eligible.Add(garage);
                }
            }

            bool allGarages = listBoxGarages.SelectedItems.Count == 0;
            if (!ConfirmModernGarageOperationH10(
                    "Melhorar",
                    eligible.Count,
                    selected.Count,
                    allGarages))
            {
                return;
            }

            int topIndex = listBoxGarages.TopIndex;
            foreach (Garages garage in eligible)
            {
                int targetStatus = garage.GarageStatus == 6 ? 2 : 3;
                ApplyModernGarageStatusH101(garage, targetStatus);
            }

            FillGaragesList(topIndex);
            RefreshModernHeadquartersChoicesH12();
            MarkModernGarageChangesH10("Melhoria de garagem", eligible.Count);
        }

        private void ModernDowngradeGaragesH10(object sender, EventArgs e)
        {
            string reason;
            if (!CanModifyModernGaragesH10(out reason))
            {
                MessageBox.Show(
                    reason,
                    "TS SE Tool H12",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            List<Garages> selected = GetSelectedModernGaragesH10();
            List<Garages> eligible = new List<Garages>();
            Dictionary<string, int> targets =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (Garages garage in selected)
            {
                int targetStatus;
                string garageReason;
                if (CanSafelyDowngradeGarageH10(
                        garage,
                        out targetStatus,
                        out garageReason))
                {
                    eligible.Add(garage);
                    targets[garage.GarageName] = targetStatus;
                }
                else if (garage.GarageStatus == 3 ||
                         (garage.GarageStatus == 2 &&
                          String.Equals(
                              garage.GarageName,
                              SiiNunitData.Player.hq_city,
                              StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(
                        "A garagem " + garage.GarageNameTranslated +
                        " não pode ser reduzida: " + garageReason,
                        "Redução de garagem bloqueada",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            bool allGarages = listBoxGarages.SelectedItems.Count == 0;
            if (!ConfirmModernGarageOperationH10(
                    "Reduzir",
                    eligible.Count,
                    selected.Count,
                    allGarages))
            {
                return;
            }

            int topIndex = listBoxGarages.TopIndex;
            foreach (Garages garage in eligible)
                ApplyModernGarageStatusH101(garage, targets[garage.GarageName]);

            FillGaragesList(topIndex);
            RefreshModernHeadquartersChoicesH12();
            MarkModernGarageChangesH10("Redução de garagem", eligible.Count);
        }

        private string GetLeadingWhitespaceH101(string line)
        {
            if (String.IsNullOrEmpty(line))
                return "";

            int index = 0;
            while (index < line.Length && Char.IsWhiteSpace(line[index]))
                index++;

            return line.Substring(0, index);
        }

        private string ToModernGarageReferenceH101(string value)
        {
            string normalized = NormalizeModernReference(value);
            return String.IsNullOrWhiteSpace(normalized)
                ? "null"
                : normalized;
        }

        private bool TryGetModernGarageBlockNameH101(
            string trimmed,
            out string garageName)
        {
            garageName = "";
            if (String.IsNullOrWhiteSpace(trimmed) ||
                !trimmed.StartsWith("garage :", StringComparison.Ordinal) ||
                !trimmed.EndsWith("{", StringComparison.Ordinal))
            {
                return false;
            }

            int colon = trimmed.IndexOf(':');
            int brace = trimmed.LastIndexOf('{');
            if (colon < 0 || brace <= colon)
                return false;

            string unitId = trimmed.Substring(
                colon + 1,
                brace - colon - 1).Trim();
            if (!unitId.StartsWith(
                    "garage.",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            garageName = unitId.Substring("garage.".Length).Trim();
            return !String.IsNullOrWhiteSpace(garageName);
        }

        private void AddModernGarageArrayH101(
            List<string> output,
            string originalLine,
            string tag,
            List<string> values)
        {
            string indent = GetLeadingWhitespaceH101(originalLine);
            output.Add(
                indent + tag + ": " +
                values.Count.ToString(CultureInfo.InvariantCulture));

            for (int index = 0; index < values.Count; index++)
            {
                output.Add(
                    indent + tag + "[" +
                    index.ToString(CultureInfo.InvariantCulture) +
                    "]: " + ToModernGarageReferenceH101(values[index]));
            }
        }

        private string PatchModernGarageDataH101(string input)
        {
            string headquartersOutput =
                PatchModernHeadquartersDataH12(input);

            if (String.IsNullOrWhiteSpace(headquartersOutput) ||
                ModernGarageChangedNamesH101.Count == 0)
            {
                return headquartersOutput;
            }

            Dictionary<string, Garages> changed =
                new Dictionary<string, Garages>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (string garageName in ModernGarageChangedNamesH101)
            {
                Garages garage = GaragesList.Find(
                    item => item != null &&
                    String.Equals(
                        item.GarageName,
                        garageName,
                        StringComparison.OrdinalIgnoreCase));
                if (garage == null)
                {
                    throw new InvalidOperationException(
                        "A garagem alterada não está mais disponível: " + garageName);
                }

                changed[garageName] = garage;
            }

            string normalized =
                headquartersOutput.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = normalized.Split('\n');
            List<string> output = new List<string>(lines.Length + 64);
            HashSet<string> patched =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Garages current = null;
            string currentName = "";
            bool skipVehicles = false;
            bool skipDrivers = false;
            bool sawVehicles = false;
            bool sawDrivers = false;
            bool sawStatus = false;

            foreach (string originalLine in lines)
            {
                string line = originalLine ?? "";
                string trimmed = line.Trim();

                if (current == null)
                {
                    string candidateName;
                    if (TryGetModernGarageBlockNameH101(
                            trimmed,
                            out candidateName) &&
                        changed.ContainsKey(candidateName))
                    {
                        current = changed[candidateName];
                        currentName = candidateName;
                        skipVehicles = false;
                        skipDrivers = false;
                        sawVehicles = false;
                        sawDrivers = false;
                        sawStatus = false;
                    }

                    output.Add(line);
                    continue;
                }

                if (trimmed == "}")
                {
                    if (!sawVehicles || !sawDrivers || !sawStatus)
                    {
                        throw new InvalidOperationException(
                            "O bloco da garagem " + currentName +
                            " não contém vehicles, drivers e status completos.");
                    }

                    patched.Add(currentName);
                    current = null;
                    currentName = "";
                    skipVehicles = false;
                    skipDrivers = false;
                    output.Add(line);
                    continue;
                }

                int colonPosition = trimmed.IndexOf(':');
                if (colonPosition < 0)
                {
                    output.Add(line);
                    continue;
                }

                string tag = trimmed.Substring(0, colonPosition).Trim();

                if (skipVehicles &&
                    tag.StartsWith("vehicles[", StringComparison.Ordinal))
                {
                    continue;
                }
                skipVehicles = false;

                if (skipDrivers &&
                    tag.StartsWith("drivers[", StringComparison.Ordinal))
                {
                    continue;
                }
                skipDrivers = false;

                if (tag == "vehicles")
                {
                    AddModernGarageArrayH101(
                        output,
                        line,
                        "vehicles",
                        current.Vehicles);
                    sawVehicles = true;
                    skipVehicles = true;
                    continue;
                }

                if (tag == "drivers")
                {
                    AddModernGarageArrayH101(
                        output,
                        line,
                        "drivers",
                        current.Drivers);
                    sawDrivers = true;
                    skipDrivers = true;
                    continue;
                }

                if (tag == "status")
                {
                    output.Add(
                        GetLeadingWhitespaceH101(line) +
                        "status: " +
                        current.GarageStatus.ToString(
                            CultureInfo.InvariantCulture));
                    sawStatus = true;
                    continue;
                }

                output.Add(line);
            }

            if (current != null)
            {
                throw new InvalidOperationException(
                    "O bloco da garagem " + currentName + " não foi encerrado corretamente.");
            }

            foreach (string garageName in changed.Keys)
            {
                if (!patched.Contains(garageName))
                {
                    throw new InvalidOperationException(
                        "O bloco da garagem alterada não foi localizado no texto original: garage." + garageName);
                }
            }

            IO_Utilities.LogWriter(
                "RC5.1H12 patched " +
                patched.Count.ToString(CultureInfo.InvariantCulture) +
                " modern garage block(s) surgically.");

            return String.Join(Environment.NewLine, output);
        }

        private TableLayoutPanel FindModernGarageActionAreaH10()
        {
            if (tabPageCompany == null)
                return null;

            Control[] matches =
                tabPageCompany.Controls.Find(
                    "tableLayoutPanel6",
                    true);

            if (matches == null || matches.Length != 1)
                return null;

            return matches[0] as TableLayoutPanel;
        }


        private void EnsureModernGarageControlsH10()
        {
            if (!IsModernCompatibilityMode() ||
                ModernGarageUiRefreshingH10)
            {
                return;
            }

            ModernGarageUiRefreshingH10 = true;
            try
            {
                RestoreOriginalCompanyActionAreaH9();

                TableLayoutPanel actionArea =
                    FindModernGarageActionAreaH10();
                if (actionArea == null)
                    return;

                tabPageCompany.Enabled = true;
                listBoxGarages.Enabled = true;
                listBoxGarages.TabStop = true;

                comboBoxUserCompanyHQcity.Enabled = true;
                comboBoxUserCompanyHQcity.TabStop = true;
                comboBoxUserCompanyHQcity.DropDownStyle =
                    ComboBoxStyle.DropDownList;

                actionArea.Visible = true;
                actionArea.Enabled = true;
                actionArea.BringToFront();

                buttonUserCompanyGaragesBuy.Visible = true;
                buttonUserCompanyGaragesBuy.Enabled = true;
                buttonUserCompanyGaragesBuy.TabStop = true;

                buttonUserCompanyGaragesUpgrade.Visible = true;
                buttonUserCompanyGaragesUpgrade.Enabled = true;
                buttonUserCompanyGaragesUpgrade.TabStop = true;

                buttonUserCompanyGaragesBuyDowngrade.Visible = true;
                buttonUserCompanyGaragesBuyDowngrade.Enabled = true;
                buttonUserCompanyGaragesBuyDowngrade.TabStop = true;

                buttonUserCompanyGaragesSell.Visible = true;
                buttonUserCompanyGaragesSell.Enabled = true;
                buttonUserCompanyGaragesSell.TabStop = true;

                buttonUserCompanyGaragesManage.Enabled = false;
                buttonUserCompanyGaragesManage.TabStop = false;

                toolTipMain.SetToolTip(
                    comboBoxUserCompanyHQcity,
                    "Altera a cidade-sede para outra garagem adquirida. A mudança somente é gravada ao clicar em Salvar.");
                toolTipMain.SetToolTip(
                    buttonUserCompanyGaragesBuy,
                    "Compra as garagens não adquiridas selecionadas. Sem seleção, verifica toda a lista.");
                toolTipMain.SetToolTip(
                    buttonUserCompanyGaragesUpgrade,
                    "Melhora garagens Tiny/Small selecionadas. Sem seleção, verifica toda a lista.");
                toolTipMain.SetToolTip(
                    buttonUserCompanyGaragesBuyDowngrade,
                    "Reduz somente quando nenhuma vaga ocupada seria removida.");
                toolTipMain.SetToolTip(
                    buttonUserCompanyGaragesSell,
                    "Vende somente garagens não-sede totalmente vazias. A seleção explícita é obrigatória.");
            }
            finally
            {
                ModernGarageUiRefreshingH10 = false;
            }

            AttachModernHeadquartersEventH12();
        }


        private void ModernGarageUiActivationH10(
            object sender,
            EventArgs e)
        {
            EnsureModernGarageControlsH10();
        }


        private void AttachModernGarageEventsH10()
        {
            if (!ModernGarageEventsAttachedH10)
            {
                buttonUserCompanyGaragesBuy.Click -=
                    buttonGaragesBuy_Click;
                buttonUserCompanyGaragesBuy.Click -=
                    ModernBuyGaragesH10;
                buttonUserCompanyGaragesBuy.Click +=
                    ModernBuyGaragesH10;

                buttonUserCompanyGaragesUpgrade.Click -=
                    buttonGaragesUpgrade_Click;
                buttonUserCompanyGaragesUpgrade.Click -=
                    ModernUpgradeGaragesH10;
                buttonUserCompanyGaragesUpgrade.Click +=
                    ModernUpgradeGaragesH10;

                buttonUserCompanyGaragesBuyDowngrade.Click -=
                    buttonGaragesDowngrade_Click;
                buttonUserCompanyGaragesBuyDowngrade.Click -=
                    ModernDowngradeGaragesH10;
                buttonUserCompanyGaragesBuyDowngrade.Click +=
                    ModernDowngradeGaragesH10;

                buttonUserCompanyGaragesSell.Click -=
                    buttonGaragesSell_Click;
                buttonUserCompanyGaragesSell.Click -=
                    ModernSellGaragesH11;
                buttonUserCompanyGaragesSell.Click +=
                    ModernSellGaragesH11;

                tabPageCompany.Enter -=
                    ModernGarageUiActivationH10;
                tabPageCompany.Enter +=
                    ModernGarageUiActivationH10;
                tabPageCompany.EnabledChanged -=
                    ModernGarageUiActivationH10;
                tabPageCompany.EnabledChanged +=
                    ModernGarageUiActivationH10;
                tabControlMain.SelectedIndexChanged -=
                    ModernGarageUiActivationH10;
                tabControlMain.SelectedIndexChanged +=
                    ModernGarageUiActivationH10;

                ModernGarageEventsAttachedH10 = true;
            }

            AttachModernHeadquartersEventH12();
        }



        private void InitializeModernGaragesH10()
        {
            if (!IsModernCompatibilityMode())
                return;

            ModernGarageChangesPendingH10 = false;
            ModernGarageChangedNamesH101.Clear();

            ModernHeadquartersChangesPendingH12 = false;
            ModernHeadquartersBaselineH12 =
                SiiNunitData == null ||
                SiiNunitData.Player == null
                    ? ""
                    : NormalizeModernCityIdH12(
                        SiiNunitData.Player.hq_city);

            AttachModernGarageEventsH10();
            RefreshModernHeadquartersChoicesH12();
            EnsureModernGarageControlsH10();
            InitializeModernMarketsH13R();

            IO_Utilities.LogWriter(
                "RC5.1H12.1 headquarters event restricted to user-committed selection; H11 garage operations initialized.");
        }

    }
}
