using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using TS_SE_Tool.Utilities;

namespace TS_SE_Tool
{
    public partial class FormMain
    {
        private bool ModernVisitedCitiesEventsAttachedH9;
        private bool ModernVisitedCitiesUiRefreshingH9;
        private bool ModernCompanyLayoutCapturedH9;
        private Padding ModernCompanyOriginalPaddingH9;

        private TabPage FindModernCompanyTabH9()
        {
            if (tabControlMain == null)
                return null;

            return tabControlMain.TabPages["tabPageCompany"];
        }

        private bool CanModifyModernVisitedCitiesH9(out string reason)
        {
            reason = "";

            if (!IsModernCompatibilityMode())
            {
                reason = "A função H9 é exclusiva do modo de compatibilidade moderno.";
                return false;
            }

            if (SiiNunitData == null ||
                SiiNunitData.Economy == null ||
                SiiNunitData.Player == null)
            {
                reason = "Os dados do save ainda não estão carregados.";
                return false;
            }

            if (!String.IsNullOrWhiteSpace(
                    NormalizeModernReference(
                        SiiNunitData.Player.current_job)))
            {
                reason = "Finalize ou cancele a entrega ativa antes de editar as cidades visitadas.";
                return false;
            }

            if (!String.IsNullOrWhiteSpace(
                    ModernPendingTruckSwitchTargetTruckId))
            {
                reason = "Salve ou descarte a troca de caminhão pendente antes de editar as cidades.";
                return false;
            }

            if (HasUnsavedModernFleetChangesH6())
            {
                reason = "Salve ou descarte as alterações de manutenção da frota antes de editar as cidades.";
                return false;
            }

            if (ModernConvoyStatusLabelH8 != null &&
                ModernConvoyStatusLabelH8.Text != null &&
                ModernConvoyStatusLabelH8.Text.IndexOf(
                    "em memória",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reason = "Salve ou descarte a importação de posição/rota H8 antes de editar as cidades.";
                return false;
            }

            return true;
        }

        private void ModernVisitCitiesH9(object sender, EventArgs e)
        {
            string reason;
            if (!CanModifyModernVisitedCitiesH9(out reason))
            {
                MessageBox.Show(
                    reason,
                    "TS SE Tool H9",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            bool allCities = listBoxVisitedCities.SelectedItems.Count == 0;
            int affected = allCities
                ? listBoxVisitedCities.Items.Count
                : listBoxVisitedCities.SelectedItems.Count;

            string question = allCities
                ? "Nenhuma cidade está selecionada. Marcar TODAS as " + affected + " cidades da lista como visitadas?"
                : "Marcar " + affected + " cidade(s) selecionada(s) como visitada(s)?";

            DialogResult confirmation = MessageBox.Show(
                question + "\r\n\r\nA alteração será preparada em memória e somente será gravada ao clicar em Salvar.",
                "Confirmar cidades visitadas H9",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (confirmation != DialogResult.Yes)
                return;

            buttonCitiesVisit_Click(sender, e);

            MessageBox.Show(
                "Cidades marcadas em memória. Clique em Salvar para gravar no TESTE_TSSET.",
                "TS SE Tool H9",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ModernUnvisitCitiesH9(object sender, EventArgs e)
        {
            string reason;
            if (!CanModifyModernVisitedCitiesH9(out reason))
            {
                MessageBox.Show(
                    reason,
                    "TS SE Tool H9",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            bool allCities = listBoxVisitedCities.SelectedItems.Count == 0;
            int affected = allCities
                ? listBoxVisitedCities.Items.Count
                : listBoxVisitedCities.SelectedItems.Count;

            string question = allCities
                ? "Nenhuma cidade está selecionada. Desmarcar TODAS as " + affected + " cidades da lista?"
                : "Desmarcar " + affected + " cidade(s) selecionada(s)?";

            DialogResult confirmation = MessageBox.Show(
                question + "\r\n\r\nA alteração será preparada em memória e somente será gravada ao clicar em Salvar.",
                "Confirmar cidades não visitadas H9",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirmation != DialogResult.Yes)
                return;

            buttonCitiesUnVisit_Click(sender, e);

            MessageBox.Show(
                "Cidades desmarcadas em memória. Clique em Salvar para gravar no TESTE_TSSET.",
                "TS SE Tool H9",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void RestoreOriginalCompanyActionAreaH9()
        {
            if (tabPageCompany == null ||
                tableLayoutPanel5 == null)
            {
                return;
            }

            if (!ModernCompanyLayoutCapturedH9)
            {
                ModernCompanyOriginalPaddingH9 =
                    tabPageCompany.Padding;
                ModernCompanyLayoutCapturedH9 = true;
            }

            int reservedBottom = 0;
            if (ModernFleetOperationsGroupH7 != null &&
                ModernFleetOperationsGroupH7.Visible)
            {
                reservedBottom =
                    ModernFleetOperationsGroupH7.Height + 6;
            }

            int desiredBottom =
                ModernCompanyOriginalPaddingH9.Bottom +
                reservedBottom;

            Padding currentPadding = tabPageCompany.Padding;
            if (currentPadding.Left !=
                    ModernCompanyOriginalPaddingH9.Left ||
                currentPadding.Top !=
                    ModernCompanyOriginalPaddingH9.Top ||
                currentPadding.Right !=
                    ModernCompanyOriginalPaddingH9.Right ||
                currentPadding.Bottom != desiredBottom)
            {
                tabPageCompany.Padding = new Padding(
                    ModernCompanyOriginalPaddingH9.Left,
                    ModernCompanyOriginalPaddingH9.Top,
                    ModernCompanyOriginalPaddingH9.Right,
                    desiredBottom);
            }

            tableLayoutPanel5.Visible = true;
            tableLayoutPanel5.Enabled = true;
            tableLayoutPanel5.BringToFront();

            buttonUserCompanyCitiesVisit.Visible = true;
            buttonUserCompanyCitiesUnVisit.Visible = true;

            if (ModernFleetOperationsGroupH7 != null)
            {
                ModernFleetOperationsGroupH7.Dock =
                    DockStyle.None;
                ModernFleetOperationsGroupH7.Anchor =
                    AnchorStyles.Left |
                    AnchorStyles.Right |
                    AnchorStyles.Bottom;

                int panelLeft =
                    ModernCompanyOriginalPaddingH9.Left;
                int panelTop = Math.Max(
                    ModernCompanyOriginalPaddingH9.Top,
                    tabPageCompany.ClientSize.Height -
                    ModernCompanyOriginalPaddingH9.Bottom -
                    ModernFleetOperationsGroupH7.Height);
                int panelWidth = Math.Max(
                    0,
                    tabPageCompany.ClientSize.Width -
                    ModernCompanyOriginalPaddingH9.Left -
                    ModernCompanyOriginalPaddingH9.Right);

                ModernFleetOperationsGroupH7.SetBounds(
                    panelLeft,
                    panelTop,
                    panelWidth,
                    ModernFleetOperationsGroupH7.Height);
                ModernFleetOperationsGroupH7.BringToFront();
            }

            tabPageCompany.PerformLayout();
        }

        private void EnsureModernVisitedCityControlsH9()
        {
            if (!IsModernCompatibilityMode() ||
                ModernVisitedCitiesUiRefreshingH9)
                return;

            ModernVisitedCitiesUiRefreshingH9 = true;
            try
            {
                TabPage companyTab = FindModernCompanyTabH9();
                if (companyTab == null)
                    return;

                RestoreOriginalCompanyActionAreaH9();

                companyTab.Enabled = true;
                listBoxVisitedCities.Enabled = true;
                listBoxVisitedCities.TabStop = true;
                buttonUserCompanyCitiesVisit.Enabled = true;
                buttonUserCompanyCitiesVisit.TabStop = true;
                buttonUserCompanyCitiesUnVisit.Enabled = true;
                buttonUserCompanyCitiesUnVisit.TabStop = true;
            }
            finally
            {
                ModernVisitedCitiesUiRefreshingH9 = false;
            }
        }

        private void ModernVisitedCitiesUiActivationH9(
            object sender,
            EventArgs e)
        {
            EnsureModernVisitedCityControlsH9();
        }

        private void AttachModernVisitedCityEventsH9()
        {
            if (ModernVisitedCitiesEventsAttachedH9)
                return;

            TabPage companyTab = FindModernCompanyTabH9();
            if (companyTab == null)
                return;

            buttonUserCompanyCitiesVisit.Click -= buttonCitiesVisit_Click;
            buttonUserCompanyCitiesVisit.Click -= ModernVisitCitiesH9;
            buttonUserCompanyCitiesVisit.Click += ModernVisitCitiesH9;

            buttonUserCompanyCitiesUnVisit.Click -= buttonCitiesUnVisit_Click;
            buttonUserCompanyCitiesUnVisit.Click -= ModernUnvisitCitiesH9;
            buttonUserCompanyCitiesUnVisit.Click += ModernUnvisitCitiesH9;

            companyTab.Enter += ModernVisitedCitiesUiActivationH9;
            companyTab.EnabledChanged += ModernVisitedCitiesUiActivationH9;
            tabControlMain.SelectedIndexChanged +=
                ModernVisitedCitiesUiActivationH9;

            tabPageCompany.SizeChanged -=
                ModernVisitedCitiesUiActivationH9;
            tabPageCompany.SizeChanged +=
                ModernVisitedCitiesUiActivationH9;

            if (ModernFleetOperationsGroupH7 != null)
            {
                ModernFleetOperationsGroupH7.SizeChanged -=
                    ModernVisitedCitiesUiActivationH9;
                ModernFleetOperationsGroupH7.SizeChanged +=
                    ModernVisitedCitiesUiActivationH9;
            }

            ModernVisitedCitiesEventsAttachedH9 = true;
        }

        private string ValidateModernVisitedCityIdH9(string value)
        {
            string cityId = (value ?? "").Trim().Trim('"');

            if (String.IsNullOrWhiteSpace(cityId) ||
                !Regex.IsMatch(cityId, "^[A-Za-z0-9_.-]+$"))
            {
                throw new InvalidOperationException(
                    "H9 encontrou um identificador de cidade inválido: " +
                    (value ?? "<null>"));
            }

            return cityId;
        }

        private void PrepareModernVisitedCitiesH9()
        {
            if (!IsModernCompatibilityMode())
                return;

            if (SiiNunitData == null || SiiNunitData.Economy == null)
            {
                throw new InvalidOperationException(
                    "H9 não encontrou os dados de economia do save.");
            }

            Dictionary<string, bool> knownStates =
                new Dictionary<string, bool>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (City city in CitiesList)
            {
                if (city == null)
                    continue;

                string cityId =
                    ValidateModernVisitedCityIdH9(city.CityName);
                bool previousState;

                if (knownStates.TryGetValue(cityId, out previousState))
                    knownStates[cityId] = previousState || city.Visited;
                else
                    knownStates.Add(cityId, city.Visited);
            }

            List<string> oldNames =
                new List<string>(SiiNunitData.Economy.visited_cities);
            List<int> oldCounts =
                new List<int>(SiiNunitData.Economy.visited_cities_count);
            List<string> newNames = new List<string>();
            List<int> newCounts = new List<int>();
            HashSet<string> added =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < oldNames.Count; index++)
            {
                string cityId =
                    ValidateModernVisitedCityIdH9(oldNames[index]);

                bool knownVisited;
                if (knownStates.TryGetValue(cityId, out knownVisited) &&
                    !knownVisited)
                {
                    continue;
                }

                if (!added.Add(cityId))
                    continue;

                int visitCount =
                    index < oldCounts.Count && oldCounts[index] > 0
                        ? oldCounts[index]
                        : 1;

                newNames.Add(cityId);
                newCounts.Add(visitCount);
            }

            foreach (City city in CitiesList)
            {
                if (city == null || !city.Visited)
                    continue;

                string cityId =
                    ValidateModernVisitedCityIdH9(city.CityName);

                if (!added.Add(cityId))
                    continue;

                int visitCount = 1;
                foreach (VisitedCity visited in VisitedCities)
                {
                    if (visited != null &&
                        String.Equals(
                            visited.Name,
                            cityId,
                            StringComparison.OrdinalIgnoreCase) &&
                        visited.VisitCount > 0)
                    {
                        visitCount = visited.VisitCount;
                        break;
                    }
                }

                newNames.Add(cityId);
                newCounts.Add(visitCount);
            }

            SiiNunitData.Economy.visited_cities.Clear();
            SiiNunitData.Economy.visited_cities.AddRange(newNames);
            SiiNunitData.Economy.visited_cities_count.Clear();
            SiiNunitData.Economy.visited_cities_count.AddRange(newCounts);

            string previousLast =
                (SiiNunitData.Economy.last_visited_city ?? "")
                    .Trim()
                    .Trim('"');

            if (newNames.Count == 0)
            {
                SiiNunitData.Economy.last_visited_city = "null";
            }
            else if (!added.Contains(previousLast))
            {
                SiiNunitData.Economy.last_visited_city =
                    newNames[newNames.Count - 1];
            }
            else
            {
                SiiNunitData.Economy.last_visited_city = previousLast;
            }
        }

        private void InitializeModernVisitedCitiesH9()
        {
            if (!IsModernCompatibilityMode())
                return;

            AttachModernVisitedCityEventsH9();
            EnsureModernVisitedCityControlsH9();

            IO_Utilities.LogWriter(
                "RC5.1H9.3 original visited-cities workflow and safe parent layout initialized.");
        }
    }
}