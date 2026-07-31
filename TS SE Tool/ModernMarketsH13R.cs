using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Windows.Forms;
using TS_SE_Tool.Utilities;

namespace TS_SE_Tool
{
    public partial class FormMain
    {
        private sealed class ModernMarketEventInfoH13R
        {
            internal string Id;
            internal uint Time;
            internal string UnitLink;
            internal int Param;
            internal int OriginalQueueIndex;
        }

        private sealed class ModernMarketOfferSlotH13R
        {
            internal string OfferId;
            internal int SlotIndex;
            internal string EventId;
        }

        private bool ModernMarketEventsAttachedH13R;
        private object ModernMarketDataIdentityH13R;
        private readonly Dictionary<string, uint[]> ModernCargoSeedUpdatesH13R =
            new Dictionary<string, uint[]>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> ModernCargoCompanyByCityH13R =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool ModernFreightSourceFilterBusyH13R;
        private bool ModernCargoSelectionBusyH13R;

        private const string ModernCargoRandomCompanyButtonNameH13R =
            "buttonCargoMarketRandomizeCargoCompany";
        private const string ModernCargoResetCompanyButtonNameH13R =
            "buttonCargoMarketResetCargoCompany";
        private const string ModernCargoRandomCityButtonNameH13R =
            "buttonCargoMarketRandomizeCargoCity";
        private const string ModernCargoResetCityButtonNameH13R =
            "buttonCargoMarketResetCargoCity";

        private T FindModernMarketControlH13R<T>(string name)
            where T : Control
        {
            Control[] matches = Controls.Find(name, true);
            if (matches == null || matches.Length == 0)
                return null;

            return matches[0] as T;
        }

        private ComboBox FindModernCargoComboInTreeH13R(
            Control root,
            string valueMember)
        {
            if (root == null)
                return null;

            foreach (Control control in root.Controls)
            {
                ComboBox combo = control as ComboBox;
                if (combo != null &&
                    String.Equals(
                        combo.ValueMember ?? "",
                        valueMember ?? "",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return combo;
                }

                ComboBox nested =
                    FindModernCargoComboInTreeH13R(
                        control,
                        valueMember);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private ComboBox FindModernCargoComboH13R(
            string valueMember)
        {
            TabPage cargoTab =
                FindModernMarketControlH13R<TabPage>(
                    "tabPageCargoMarket");
            ComboBox result =
                FindModernCargoComboInTreeH13R(
                    cargoTab,
                    valueMember);
            if (result != null)
                return result;

            foreach (Control control in Controls)
            {
                result =
                    FindModernCargoComboInTreeH13R(
                        control,
                        valueMember);
                if (result != null)
                    return result;
            }

            return null;
        }

        private bool TryGetModernCargoSelectionH13R(
            bool requireCompany,
            out string cityId,
            out string companyId)
        {
            cityId = "";
            companyId = "";

            ComboBox cityCombo =
                FindModernCargoComboH13R("City");
            ComboBox companyCombo =
                FindModernCargoComboH13R("Company");

            if (cityCombo == null ||
                cityCombo.SelectedValue == null)
            {
                ShowModernMarketMessageH13R(
                    "Selecione uma cidade no Cargo Market.",
                    true);
                return false;
            }

            cityId = cityCombo.SelectedValue.ToString();

            if (!requireCompany)
                return true;

            if (companyCombo == null ||
                companyCombo.SelectedValue == null)
            {
                ShowModernMarketMessageH13R(
                    "Selecione uma empresa no Cargo Market.",
                    true);
                return false;
            }

            companyId = companyCombo.SelectedValue.ToString();
            return true;
        }

        private void EnableModernCargoSelectionControlsH13R(
            Control root)
        {
            if (root == null)
                return;

            foreach (Control control in root.Controls)
            {
                if (control is ComboBox ||
                    control is ListBox)
                {
                    control.Enabled = true;
                    control.TabStop = true;
                }

                if (control.HasChildren)
                    EnableModernCargoSelectionControlsH13R(control);
            }
        }

        private string NormalizeModernMarketIdH13R(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "";

            return value.Trim().Trim('"');
        }

        private string BuildModernCompanyKeyH13R(
            string company,
            string city)
        {
            string normalizedCompany =
                NormalizeModernMarketIdH13R(company);
            string normalizedCity =
                NormalizeModernMarketIdH13R(city);

            if (String.IsNullOrWhiteSpace(normalizedCompany) ||
                String.IsNullOrWhiteSpace(normalizedCity))
            {
                return "";
            }

            return "company.volatile." +
                   normalizedCompany + "." + normalizedCity;
        }

        private string ReadModernFreightBoundValueH13R(
            ComboBox combo,
            string valueMember)
        {
            if (combo == null)
                return "";

            object selectedValue = combo.SelectedValue;
            if (selectedValue != null && selectedValue != DBNull.Value)
            {
                string directValue =
                    NormalizeModernMarketIdH13R(
                        selectedValue.ToString());
                if (!String.IsNullOrWhiteSpace(directValue))
                    return directValue;
            }

            DataRowView rowView = combo.SelectedItem as DataRowView;
            if (rowView != null &&
                rowView.Row != null &&
                rowView.Row.Table != null &&
                rowView.Row.Table.Columns.Contains(valueMember))
            {
                object rowValue = rowView[valueMember];
                if (rowValue != null && rowValue != DBNull.Value)
                {
                    string boundValue =
                        NormalizeModernMarketIdH13R(
                            rowValue.ToString());
                    if (!String.IsNullOrWhiteSpace(boundValue))
                        return boundValue;
                }
            }

            DataRow row = combo.SelectedItem as DataRow;
            if (row != null &&
                row.Table != null &&
                row.Table.Columns.Contains(valueMember))
            {
                object rowValue = row[valueMember];
                if (rowValue != null && rowValue != DBNull.Value)
                {
                    string boundValue =
                        NormalizeModernMarketIdH13R(
                            rowValue.ToString());
                    if (!String.IsNullOrWhiteSpace(boundValue))
                        return boundValue;
                }
            }

            return "";
        }

        private string ResolveModernFreightCityFromTextH13R(
            ComboBox combo)
        {
            string boundValue =
                ReadModernFreightBoundValueH13R(
                    combo,
                    "City");
            if (!String.IsNullOrWhiteSpace(boundValue))
                return boundValue;

            string visibleText =
                NormalizeModernMarketIdH13R(
                    combo == null ? "" : combo.Text);
            if (String.IsNullOrWhiteSpace(visibleText) ||
                CitiesList == null)
            {
                return "";
            }

            City city = CitiesList.Find(
                item => item != null &&
                (String.Equals(
                     NormalizeModernMarketIdH13R(item.CityName),
                     visibleText,
                     StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(
                     NormalizeModernMarketIdH13R(item.CityNameTranslated),
                     visibleText,
                     StringComparison.CurrentCultureIgnoreCase)));

            return city == null
                ? ""
                : NormalizeModernMarketIdH13R(city.CityName);
        }

        private string ResolveModernFreightCompanyFromTextH13R(
            ComboBox combo,
            string cityId)
        {
            string boundValue =
                ReadModernFreightBoundValueH13R(
                    combo,
                    "Company");
            if (!String.IsNullOrWhiteSpace(boundValue))
                return boundValue;

            string visibleText =
                NormalizeModernMarketIdH13R(
                    combo == null ? "" : combo.Text);
            string normalizedCity =
                NormalizeModernMarketIdH13R(cityId);
            if (String.IsNullOrWhiteSpace(visibleText) ||
                String.IsNullOrWhiteSpace(normalizedCity) ||
                CitiesList == null)
            {
                return "";
            }

            City city = CitiesList.Find(
                item => item != null &&
                String.Equals(
                    NormalizeModernMarketIdH13R(item.CityName),
                    normalizedCity,
                    StringComparison.OrdinalIgnoreCase));
            if (city == null)
                return "";

            List<Company> companies = city.ReturnCompanies();
            if (companies == null)
                return "";

            Company company = companies.Find(
                item => item != null &&
                (String.Equals(
                     NormalizeModernMarketIdH13R(item.CompanyName),
                     visibleText,
                     StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(
                     NormalizeModernMarketIdH13R(item.CompanyNameTranslated),
                     visibleText,
                     StringComparison.CurrentCultureIgnoreCase)));

            return company == null
                ? ""
                : NormalizeModernMarketIdH13R(company.CompanyName);
        }

        private bool TrySelectModernFreightBoundValueH13R(
            ComboBox combo,
            string valueMember,
            string value)
        {
            string normalizedValue =
                NormalizeModernMarketIdH13R(value);
            if (combo == null ||
                String.IsNullOrWhiteSpace(normalizedValue))
            {
                return false;
            }

            if (!String.Equals(
                    combo.ValueMember ?? "",
                    valueMember,
                    StringComparison.OrdinalIgnoreCase))
            {
                combo.ValueMember = valueMember;
            }

            try
            {
                combo.SelectedValue = normalizedValue;
            }
            catch
            {
            }

            string selectedValue =
                ReadModernFreightBoundValueH13R(
                    combo,
                    valueMember);
            if (String.Equals(
                    selectedValue,
                    normalizedValue,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            for (int index = 0;
                 index < combo.Items.Count;
                 index++)
            {
                object item = combo.Items[index];
                string itemValue = "";

                DataRowView rowView = item as DataRowView;
                if (rowView != null &&
                    rowView.Row != null &&
                    rowView.Row.Table != null &&
                    rowView.Row.Table.Columns.Contains(valueMember))
                {
                    object rawValue = rowView[valueMember];
                    if (rawValue != null && rawValue != DBNull.Value)
                        itemValue = rawValue.ToString();
                }
                else
                {
                    DataRow row = item as DataRow;
                    if (row != null &&
                        row.Table != null &&
                        row.Table.Columns.Contains(valueMember))
                    {
                        object rawValue = row[valueMember];
                        if (rawValue != null && rawValue != DBNull.Value)
                            itemValue = rawValue.ToString();
                    }
                }

                if (!String.Equals(
                        NormalizeModernMarketIdH13R(itemValue),
                        normalizedValue,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                combo.SelectedIndex = index;
                break;
            }

            selectedValue =
                ReadModernFreightBoundValueH13R(
                    combo,
                    valueMember);
            return String.Equals(
                selectedValue,
                normalizedValue,
                StringComparison.OrdinalIgnoreCase);
        }

        private string DescribeModernFreightComboH13R(
            ComboBox combo)
        {
            if (combo == null)
                return "control=null";

            string selectedValue = combo.SelectedValue == null
                ? "<null>"
                : combo.SelectedValue.ToString();

            return "name=" + combo.Name +
                   ", text=\"" + (combo.Text ?? "") + "\"" +
                   ", index=" +
                   combo.SelectedIndex.ToString(
                       CultureInfo.InvariantCulture) +
                   ", value=\"" + selectedValue + "\"" +
                   ", value_member=\"" +
                   (combo.ValueMember ?? "") + "\"" +
                   ", items=" +
                   combo.Items.Count.ToString(
                       CultureInfo.InvariantCulture);
        }

        private bool NormalizeModernFreightSelectionsH13R(
            out string reason)
        {
            reason = "";

            string sourceCity =
                ResolveModernFreightCityFromTextH13R(
                    comboBoxFreightMarketSourceCity);
            string sourceCompany =
                ResolveModernFreightCompanyFromTextH13R(
                    comboBoxFreightMarketSourceCompany,
                    sourceCity);
            string destinationCity =
                ResolveModernFreightCityFromTextH13R(
                    comboBoxFreightMarketDestinationCity);
            string destinationCompany =
                ResolveModernFreightCompanyFromTextH13R(
                    comboBoxFreightMarketDestinationCompany,
                    destinationCity);

            IO_Utilities.LogWriter(
                "RC5.1H13R.8.2 freight UI selection before normalization: " +
                "source_city=[" +
                DescribeModernFreightComboH13R(
                    comboBoxFreightMarketSourceCity) +
                "], source_company=[" +
                DescribeModernFreightComboH13R(
                    comboBoxFreightMarketSourceCompany) +
                "], destination_city=[" +
                DescribeModernFreightComboH13R(
                    comboBoxFreightMarketDestinationCity) +
                "], destination_company=[" +
                DescribeModernFreightComboH13R(
                    comboBoxFreightMarketDestinationCompany) +
                "].");

            List<string> missing = new List<string>();
            if (String.IsNullOrWhiteSpace(sourceCity))
                missing.Add("cidade de origem");
            if (String.IsNullOrWhiteSpace(sourceCompany))
                missing.Add("empresa de origem");
            if (String.IsNullOrWhiteSpace(destinationCity))
                missing.Add("cidade de destino");
            if (String.IsNullOrWhiteSpace(destinationCompany))
                missing.Add("empresa de destino");

            if (missing.Count > 0)
            {
                reason =
                    "A seleção visível não corresponde a um item válido em: " +
                    String.Join(", ", missing) +
                    ". Abra a lista correspondente e selecione o item novamente.";
                return false;
            }

            if (!TrySelectModernFreightBoundValueH13R(
                    comboBoxFreightMarketSourceCity,
                    "City",
                    sourceCity))
            {
                reason =
                    "A cidade de origem visível não pôde ser vinculada ao identificador interno do save.";
                return false;
            }

            RebuildModernFreightSourceCompaniesH13R();

            if (!TrySelectModernFreightBoundValueH13R(
                    comboBoxFreightMarketSourceCompany,
                    "Company",
                    sourceCompany))
            {
                reason =
                    "A empresa de origem selecionada não está disponível como origem válida neste save.";
                return false;
            }

            if (!TrySelectModernFreightBoundValueH13R(
                    comboBoxFreightMarketDestinationCity,
                    "City",
                    destinationCity))
            {
                reason =
                    "A cidade de destino visível não pôde ser vinculada ao identificador interno do save.";
                return false;
            }

            if (!TrySelectModernFreightBoundValueH13R(
                    comboBoxFreightMarketDestinationCompany,
                    "Company",
                    destinationCompany))
            {
                reason =
                    "A empresa de destino visível não pôde ser vinculada ao identificador interno do save.";
                return false;
            }

            IO_Utilities.LogWriter(
                "RC5.1H13R.8.2 freight UI selection normalized: " +
                "source=" + sourceCompany + "." + sourceCity +
                ", destination=" +
                destinationCompany + "." + destinationCity + ".");

            return true;
        }

        private string GetLeadingWhitespaceH13R(string line)
        {
            if (String.IsNullOrEmpty(line))
                return "";

            int index = 0;
            while (index < line.Length &&
                   Char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            return line.Substring(0, index);
        }

        private bool HasUnsavedModernMarketChangesH13R()
        {
            return (AddedJobsList != null &&
                    AddedJobsList.Count > 0) ||
                   ModernCargoSeedUpdatesH13R.Count > 0;
        }

        private void ShowModernMarketMessageH13R(
            string message,
            bool error)
        {
            if (toolStripStatusMessages != null)
            {
                toolStripStatusMessages.Text = message ?? "";
                toolStripStatusMessages.ForeColor =
                    error
                        ? System.Drawing.Color.Red
                        : System.Drawing.Color.Black;
            }

            if (error)
            {
                MessageBox.Show(
                    message,
                    "Mercados — modo seguro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private bool TryGetModernSaveItemH13R<T>(
            string itemId,
            out T result)
            where T : class
        {
            result = null;

            string normalizedId =
                NormalizeModernMarketIdH13R(itemId);
            if (String.IsNullOrWhiteSpace(normalizedId) ||
                SiiNunitData == null ||
                SiiNunitData.SiiNitems == null)
            {
                return false;
            }

            dynamic item;
            if (!SiiNunitData.SiiNitems.TryGetValue(
                    normalizedId,
                    out item))
            {
                return false;
            }

            result = item as T;
            return result != null;
        }

        private List<ModernMarketOfferSlotH13R>
            GetModernCompanyUsableOfferSlotsH13R(
                string companyKey)
        {
            List<ModernMarketOfferSlotH13R> result =
                new List<ModernMarketOfferSlotH13R>();

            Save.Items.Company company =
                GetModernSaveCompanyH13R(companyKey);
            Save.Items.Economy_event_Queue eventQueue = null;
            if (company == null ||
                company.job_offer == null ||
                SiiNunitData == null ||
                SiiNunitData.Economy == null ||
                !TryGetModernSaveItemH13R(
                    SiiNunitData.Economy.event_queue,
                    out eventQueue) ||
                eventQueue.data == null)
            {
                return result;
            }

            for (int slotIndex = 0;
                 slotIndex < company.job_offer.Count;
                 slotIndex++)
            {
                string offerId =
                    NormalizeModernMarketIdH13R(
                        company.job_offer[slotIndex]);
                if (String.IsNullOrWhiteSpace(offerId))
                    continue;

                Save.Items.Job_offer_Data offerData;
                if (!TryGetModernSaveItemH13R(
                        offerId,
                        out offerData))
                {
                    continue;
                }

                string matchingEventId = "";
                int matchingEvents = 0;
                foreach (string rawEventId
                         in eventQueue.data)
                {
                    string eventId =
                        NormalizeModernMarketIdH13R(rawEventId);
                    Save.Items.Economy_event economyEvent;
                    if (!TryGetModernSaveItemH13R(
                            eventId,
                            out economyEvent))
                    {
                        continue;
                    }

                    if (economyEvent.param == slotIndex &&
                        String.Equals(
                            NormalizeModernMarketIdH13R(
                                economyEvent.unit_link),
                            companyKey,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        matchingEventId = eventId;
                        matchingEvents++;
                    }
                }

                if (matchingEvents == 1)
                {
                    result.Add(
                        new ModernMarketOfferSlotH13R
                        {
                            OfferId = offerId,
                            SlotIndex = slotIndex,
                            EventId = matchingEventId
                        });
                }
            }

            return result;
        }

        private bool IsModernFreightSourceCompanyEligibleH13R(
            string cityId,
            string companyId,
            out string reason)
        {
            reason = "";
            string companyKey =
                BuildModernCompanyKeyH13R(
                    companyId,
                    cityId);
            if (String.IsNullOrWhiteSpace(companyKey))
            {
                reason = "A empresa de origem não pôde ser identificada.";
                return false;
            }

            Save.Items.Company company =
                GetModernSaveCompanyH13R(companyKey);
            if (company == null)
            {
                reason =
                    "A empresa de origem não existe neste save: " +
                    companyKey + ".";
                return false;
            }

            if (!company.discovered)
            {
                reason =
                    "A empresa de origem ainda não foi descoberta no perfil. " +
                    "Escolha uma empresa descoberta.";
                return false;
            }

            int usableSlots =
                GetModernCompanyUsableOfferSlotsH13R(
                    companyKey).Count;
            if (usableSlots <= 0)
            {
                reason =
                    "A empresa de origem não possui uma vaga real de oferta " +
                    "com job_offer_data e evento econômico correspondentes.";
                return false;
            }

            return true;
        }

        private List<Company> GetModernEligibleSourceCompaniesH13R(
            string cityId)
        {
            List<Company> result = new List<Company>();
            string normalizedCity =
                NormalizeModernMarketIdH13R(cityId);

            City city = CitiesList == null
                ? null
                : CitiesList.Find(
                    item => item != null &&
                    String.Equals(
                        NormalizeModernMarketIdH13R(item.CityName),
                        normalizedCity,
                        StringComparison.OrdinalIgnoreCase));
            if (city == null)
                return result;

            List<Company> companies = city.ReturnCompanies();
            if (companies == null)
                return result;

            foreach (Company company in companies)
            {
                if (company == null || company.Excluded)
                    continue;

                string reason;
                if (IsModernFreightSourceCompanyEligibleH13R(
                        normalizedCity,
                        company.CompanyName,
                        out reason))
                {
                    result.Add(company);
                }
            }

            result.Sort(
                delegate(Company left, Company right)
                {
                    return String.Compare(
                        left == null ? "" : left.CompanyNameTranslated,
                        right == null ? "" : right.CompanyNameTranslated,
                        StringComparison.CurrentCultureIgnoreCase);
                });
            return result;
        }

        private void RebuildModernFreightSourceCompaniesH13R()
        {
            if (ModernFreightSourceFilterBusyH13R ||
                comboBoxFreightMarketSourceCity == null ||
                comboBoxFreightMarketSourceCompany == null)
            {
                return;
            }

            string cityId =
                comboBoxFreightMarketSourceCity.SelectedValue == null
                    ? ""
                    : NormalizeModernMarketIdH13R(
                        comboBoxFreightMarketSourceCity.SelectedValue.ToString());
            string preferredCompany =
                comboBoxFreightMarketSourceCompany.SelectedValue == null
                    ? ""
                    : NormalizeModernMarketIdH13R(
                        comboBoxFreightMarketSourceCompany.SelectedValue.ToString());

            DataTable table = new DataTable();
            table.Columns.Add("Company", typeof(string));
            table.Columns.Add("CompanyName", typeof(string));

            foreach (Company company
                     in GetModernEligibleSourceCompaniesH13R(cityId))
            {
                table.Rows.Add(
                    company.CompanyName,
                    company.CompanyNameTranslated);
            }

            ModernFreightSourceFilterBusyH13R = true;
            try
            {
                comboBoxFreightMarketSourceCompany.ValueMember = "Company";
                comboBoxFreightMarketSourceCompany.DisplayMember = "CompanyName";
                comboBoxFreightMarketSourceCompany.DataSource = table;

                if (comboBoxFreightMarketSourceCompany.Items.Count == 0)
                {
                    comboBoxFreightMarketSourceCompany.SelectedIndex = -1;
                    comboBoxFreightMarketSourceCompany.Text = "";
                }
                else
                {
                    if (!String.IsNullOrWhiteSpace(preferredCompany))
                        comboBoxFreightMarketSourceCompany.SelectedValue =
                            preferredCompany;

                    if (comboBoxFreightMarketSourceCompany.SelectedIndex < 0)
                        comboBoxFreightMarketSourceCompany.SelectedIndex = 0;
                }
            }
            finally
            {
                ModernFreightSourceFilterBusyH13R = false;
            }

            IO_Utilities.LogWriter(
                "RC5.1H13R.8.2 freight source-company filter: city=" +
                cityId +
                ", eligible_companies=" +
                comboBoxFreightMarketSourceCompany.Items.Count.ToString(
                    CultureInfo.InvariantCulture) +
                ", selected=" +
                (comboBoxFreightMarketSourceCompany.SelectedValue == null
                    ? "<none>"
                    : comboBoxFreightMarketSourceCompany.SelectedValue.ToString()) +
                ".");
        }

        private void RebuildModernFreightSourceCitiesH13R()
        {
            if (ModernFreightSourceFilterBusyH13R ||
                comboBoxFreightMarketSourceCity == null ||
                comboBoxFreightMarketSourceCompany == null ||
                CitiesList == null)
            {
                return;
            }

            string preferredCity =
                comboBoxFreightMarketSourceCity.SelectedValue == null
                    ? ""
                    : NormalizeModernMarketIdH13R(
                        comboBoxFreightMarketSourceCity.SelectedValue.ToString());

            DataTable table = new DataTable();
            DataColumn cityColumn =
                new DataColumn("City", typeof(string));
            table.Columns.Add(cityColumn);
            table.PrimaryKey = new DataColumn[] { cityColumn };
            table.Columns.Add("CityName", typeof(string));

            foreach (City city in CitiesList)
            {
                if (city == null || city.Disabled)
                    continue;

                if (GetModernEligibleSourceCompaniesH13R(
                        city.CityName).Count > 0)
                {
                    table.Rows.Add(
                        city.CityName,
                        city.CityNameTranslated);
                }
            }

            table.DefaultView.Sort = "CityName ASC";

            ModernFreightSourceFilterBusyH13R = true;
            try
            {
                comboBoxFreightMarketSourceCity.ValueMember = "City";
                comboBoxFreightMarketSourceCity.DisplayMember = "CityName";
                comboBoxFreightMarketSourceCity.DataSource = table;

                if (comboBoxFreightMarketSourceCity.Items.Count == 0)
                {
                    comboBoxFreightMarketSourceCity.SelectedIndex = -1;
                    comboBoxFreightMarketSourceCity.Text = "";
                }
                else
                {
                    if (!String.IsNullOrWhiteSpace(preferredCity))
                        comboBoxFreightMarketSourceCity.SelectedValue = preferredCity;

                    if (comboBoxFreightMarketSourceCity.SelectedIndex < 0 &&
                        SiiNunitData != null &&
                        SiiNunitData.Economy != null &&
                        !String.IsNullOrWhiteSpace(
                            SiiNunitData.Economy.last_visited_city))
                    {
                        comboBoxFreightMarketSourceCity.SelectedValue =
                            SiiNunitData.Economy.last_visited_city;
                    }

                    if (comboBoxFreightMarketSourceCity.SelectedIndex < 0)
                        comboBoxFreightMarketSourceCity.SelectedIndex = 0;
                }
            }
            finally
            {
                ModernFreightSourceFilterBusyH13R = false;
            }

            IO_Utilities.LogWriter(
                "RC5.1H13R.8.2 freight source-city filter: eligible_cities=" +
                comboBoxFreightMarketSourceCity.Items.Count.ToString(
                    CultureInfo.InvariantCulture) +
                ", selected=" +
                (comboBoxFreightMarketSourceCity.SelectedValue == null
                    ? "<none>"
                    : comboBoxFreightMarketSourceCity.SelectedValue.ToString()) +
                ".");

            RebuildModernFreightSourceCompaniesH13R();
        }

        private void ModernFreightSourceCityChangedH13R(
            object sender,
            EventArgs e)
        {
            if (ModernFreightSourceFilterBusyH13R)
                return;

            RebuildModernFreightSourceCompaniesH13R();
        }

        private bool ValidateModernFreightSemanticSelectionH13R(
            out string reason)
        {
            reason = "";

            if (!NormalizeModernFreightSelectionsH13R(
                    out reason))
            {
                return false;
            }

            if (comboBoxFreightMarketSourceCity == null ||
                comboBoxFreightMarketSourceCompany == null ||
                comboBoxFreightMarketDestinationCity == null ||
                comboBoxFreightMarketDestinationCompany == null ||
                comboBoxFreightMarketSourceCity.SelectedValue == null ||
                comboBoxFreightMarketSourceCompany.SelectedValue == null ||
                comboBoxFreightMarketDestinationCity.SelectedValue == null ||
                comboBoxFreightMarketDestinationCompany.SelectedValue == null)
            {
                reason =
                    "Selecione origem e destino completos antes de adicionar o trabalho.";
                return false;
            }

            string sourceCity = NormalizeModernMarketIdH13R(
                comboBoxFreightMarketSourceCity.SelectedValue.ToString());
            string sourceCompany = NormalizeModernMarketIdH13R(
                comboBoxFreightMarketSourceCompany.SelectedValue.ToString());
            string destinationCity = NormalizeModernMarketIdH13R(
                comboBoxFreightMarketDestinationCity.SelectedValue.ToString());
            string destinationCompany = NormalizeModernMarketIdH13R(
                comboBoxFreightMarketDestinationCompany.SelectedValue.ToString());

            if (!IsModernFreightSourceCompanyEligibleH13R(
                    sourceCity,
                    sourceCompany,
                    out reason))
            {
                return false;
            }

            if (String.Equals(
                    sourceCity,
                    destinationCity,
                    StringComparison.OrdinalIgnoreCase) &&
                String.Equals(
                    sourceCompany,
                    destinationCompany,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason =
                    "Origem e destino não podem ser a mesma empresa na mesma cidade.";
                return false;
            }

            string destinationKey =
                BuildModernCompanyKeyH13R(
                    destinationCompany,
                    destinationCity);
            if (GetModernSaveCompanyH13R(destinationKey) == null)
            {
                reason =
                    "A empresa de destino não existe neste save: " +
                    destinationKey + ".";
                return false;
            }

            string[] route;
            try
            {
                route = RouteList.GetRouteData(
                    sourceCity,
                    sourceCompany,
                    destinationCity,
                    destinationCompany);
            }
            catch (Exception ex)
            {
                reason =
                    "A rota selecionada não pôde ser validada: " +
                    ex.Message;
                return false;
            }

            int rawDistance;
            if (route == null || route.Length < 7 ||
                !Int32.TryParse(
                    route[4],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out rawDistance) ||
                rawDistance <= 1 ||
                rawDistance == 11111)
            {
                reason =
                    "A rota selecionada não possui distância real válida. " +
                    "Escolha outra combinação de origem e destino.";
                return false;
            }

            int storedDistance =
                (int)(rawDistance * ProgSettingsV.TimeMultiplier);
            if (storedDistance <= 1 || storedDistance == 11111)
            {
                reason =
                    "A distância final da oferta ficaria inválida com as configurações atuais.";
                return false;
            }

            return true;
        }

        private void RememberModernCargoCompanyH13R()
        {
            if (ModernCargoSelectionBusyH13R)
                return;

            ComboBox cityCombo = FindModernCargoComboH13R("City");
            ComboBox companyCombo = FindModernCargoComboH13R("Company");
            if (cityCombo == null || companyCombo == null ||
                cityCombo.SelectedValue == null ||
                companyCombo.SelectedValue == null)
            {
                return;
            }

            ModernCargoCompanyByCityH13R[
                NormalizeModernMarketIdH13R(
                    cityCombo.SelectedValue.ToString())] =
                NormalizeModernMarketIdH13R(
                    companyCombo.SelectedValue.ToString());
        }

        private void ModernCargoCompanySelectionRememberH13R(
            object sender,
            EventArgs e)
        {
            RememberModernCargoCompanyH13R();
        }

        private void ModernCargoCitySelectionDeterministicH13R(
            object sender,
            EventArgs e)
        {
            if (ModernCargoSelectionBusyH13R)
                return;

            ComboBox cityCombo = FindModernCargoComboH13R("City");
            ComboBox companyCombo = FindModernCargoComboH13R("Company");
            if (cityCombo == null || companyCombo == null ||
                cityCombo.SelectedValue == null ||
                companyCombo.Items.Count == 0)
            {
                return;
            }

            string cityId = NormalizeModernMarketIdH13R(
                cityCombo.SelectedValue.ToString());
            string preferredCompany;

            ModernCargoSelectionBusyH13R = true;
            try
            {
                if (ModernCargoCompanyByCityH13R.TryGetValue(
                        cityId,
                        out preferredCompany) &&
                    !String.IsNullOrWhiteSpace(preferredCompany))
                {
                    companyCombo.SelectedValue = preferredCompany;
                }

                if (companyCombo.SelectedIndex < 0)
                    companyCombo.SelectedIndex = 0;
            }
            finally
            {
                ModernCargoSelectionBusyH13R = false;
            }

            RememberModernCargoCompanyH13R();
        }

        private bool CanModifyModernMarketsH13R(
            out string reason)
        {
            reason = "";

            if (!IsModernCompatibilityMode())
            {
                reason =
                    "As ferramentas modernas de mercado são exclusivas do modo de compatibilidade.";
                return false;
            }

            if (SiiNunitData == null ||
                SiiNunitData.Economy == null ||
                SiiNunitData.Player == null ||
                CitiesList == null)
            {
                reason = "Os dados do save ainda não estão carregados.";
                return false;
            }

            if (!String.IsNullOrWhiteSpace(
                    NormalizeModernReference(
                        SiiNunitData.Player.current_job)))
            {
                reason =
                    "Finalize ou cancele a entrega ativa antes de editar os mercados.";
                return false;
            }

            if (!String.IsNullOrWhiteSpace(
                    ModernPendingTruckSwitchTargetTruckId))
            {
                reason =
                    "Salve ou descarte a troca de caminhão pendente antes de editar os mercados.";
                return false;
            }

            if (HasUnsavedModernFleetChangesH6())
            {
                reason =
                    "Salve ou descarte a manutenção da frota antes de editar os mercados.";
                return false;
            }

            if (ModernConvoyStatusLabelH8 != null &&
                ModernConvoyStatusLabelH8.Text != null &&
                ModernConvoyStatusLabelH8.Text.IndexOf(
                    "em memória",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reason =
                    "Salve ou descarte a importação de posição/rota antes de editar os mercados.";
                return false;
            }

            if (HasUnsavedModernVisitedCityChangesH10())
            {
                reason =
                    "Salve ou descarte as alterações de cidades visitadas antes de editar os mercados.";
                return false;
            }

            if (HasUnsavedModernGarageChangesH10())
            {
                reason =
                    "Salve ou descarte as alterações de garagem antes de editar os mercados.";
                return false;
            }

            if (HasUnsavedModernHeadquartersChangesH12())
            {
                reason =
                    "Salve ou descarte a alteração da cidade-sede antes de editar os mercados.";
                return false;
            }

            return true;
        }

        private bool EnsureModernMarketMutationAllowedH13R()
        {
            string reason;
            if (CanModifyModernMarketsH13R(out reason))
                return true;

            ShowModernMarketMessageH13R(reason, true);
            return false;
        }

        private Save.Items.Company GetModernSaveCompanyH13R(
            string companyKey)
        {
            if (String.IsNullOrWhiteSpace(companyKey) ||
                SiiNunitData == null ||
                SiiNunitData.SiiNitems == null)
            {
                return null;
            }

            try
            {
                return SiiNunitData.SiiNitems[companyKey]
                    as Save.Items.Company;
            }
            catch
            {
                return null;
            }
        }

        private int GetModernCompanyOfferCapacityH13R(
            string companyKey)
        {
            Save.Items.Company company =
                GetModernSaveCompanyH13R(companyKey);

            if (company == null || !company.discovered)
                return 0;

            return GetModernCompanyUsableOfferSlotsH13R(
                companyKey).Count;
        }

        private bool ValidateModernFreightTargetCapacityH13R(
            bool editing,
            out string reason)
        {
            reason = "";

            if (comboBoxFreightMarketSourceCity == null ||
                comboBoxFreightMarketSourceCompany == null ||
                comboBoxFreightMarketSourceCity.SelectedValue == null ||
                comboBoxFreightMarketSourceCompany.SelectedValue == null)
            {
                return true;
            }

            string sourceCity =
                NormalizeModernMarketIdH13R(
                    comboBoxFreightMarketSourceCity.SelectedValue.ToString());
            string sourceCompany =
                NormalizeModernMarketIdH13R(
                    comboBoxFreightMarketSourceCompany.SelectedValue.ToString());
            string companyKey =
                BuildModernCompanyKeyH13R(
                    sourceCompany,
                    sourceCity);

            if (String.IsNullOrWhiteSpace(companyKey))
                return true;

            if (editing && FreightMarketJob != null)
            {
                string originalKey =
                    BuildModernCompanyKeyH13R(
                        FreightMarketJob.SourceCompany,
                        FreightMarketJob.SourceCity);

                if (!String.Equals(
                        originalKey,
                        companyKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    reason =
                        "Na edição segura, origem e empresa não podem ser alteradas. " +
                        "Exclua o trabalho e adicione outro para mudar a origem.";
                    return false;
                }
            }

            int plannedCount = 0;
            if (AddedJobsDictionary != null &&
                AddedJobsDictionary.ContainsKey(companyKey))
            {
                plannedCount =
                    AddedJobsDictionary[companyKey].Count;
            }

            if (!editing)
                plannedCount++;

            int capacity =
                GetModernCompanyOfferCapacityH13R(companyKey);

            if (capacity <= 0)
            {
                reason =
                    "A empresa de origem não possui ofertas de frete substituíveis neste save.";
                return false;
            }

            if (plannedCount > capacity)
            {
                reason =
                    "A empresa de origem possui " +
                    capacity.ToString(CultureInfo.InvariantCulture) +
                    " vaga(s) de oferta, mas a lista tentaria usar " +
                    plannedCount.ToString(CultureInfo.InvariantCulture) +
                    ". Escolha outra origem ou exclua um trabalho da lista.";
                return false;
            }

            return true;
        }

        private void ModernFreightAddOrEditH13R(
            object sender,
            EventArgs e)
        {
            if (!EnsureModernMarketMutationAllowedH13R())
                return;

            bool editing =
                FreightMarketJob != null &&
                listBoxFreightMarketAddedJobs != null &&
                !listBoxFreightMarketAddedJobs.Enabled;

            string reason;
            if (!ValidateModernFreightSemanticSelectionH13R(
                    out reason))
            {
                ShowModernMarketMessageH13R(reason, true);
                return;
            }

            if (!ValidateModernFreightTargetCapacityH13R(
                    editing,
                    out reason))
            {
                ShowModernMarketMessageH13R(reason, true);
                return;
            }

            try
            {
                int before =
                    AddedJobsList == null
                        ? 0
                        : AddedJobsList.Count;

                if (editing)
                    buttonEditJob_Click(sender, e);
                else
                    buttonAddJob_Click(sender, e);

                int after =
                    AddedJobsList == null
                        ? 0
                        : AddedJobsList.Count;

                if (after > before || editing)
                {
                    if (editing)
                        FreightMarketJob = null;

                    ShowModernMarketMessageH13R(
                        editing
                            ? "Trabalho validado e atualizado em memória. Clique em Salvar para gravar."
                            : "Trabalho validado e adicionado em memória. Clique em Salvar para gravar.",
                        false);
                }
            }
            catch (Exception ex)
            {
                ShowModernMarketMessageH13R(
                    "Não foi possível preparar o trabalho: " +
                    ex.Message,
                    true);
            }
            finally
            {
                AttachModernMarketEventsH13R();
                RebuildModernFreightSourceCitiesH13R();
                EnsureModernMarketControlsH13R();
            }
        }

        private void ModernFreightEditCommandH13R(
            object sender,
            EventArgs e)
        {
            if (!EnsureModernMarketMutationAllowedH13R())
                return;

            if (listBoxFreightMarketAddedJobs == null ||
                listBoxFreightMarketAddedJobs.SelectedItem == null)
            {
                ShowModernMarketMessageH13R(
                    "Selecione um trabalho criado nesta sessão para editar.",
                    true);
                return;
            }

            try
            {
                FM_JobList_Edit();
            }
            catch (Exception ex)
            {
                ShowModernMarketMessageH13R(
                    "Não foi possível abrir a edição do trabalho: " +
                    ex.Message,
                    true);
            }
            finally
            {
                AttachModernMarketEventsH13R();
                EnsureModernMarketControlsH13R();
            }
        }

        private void ModernFreightDeleteCommandH13R(
            object sender,
            EventArgs e)
        {
            if (!EnsureModernMarketMutationAllowedH13R())
                return;

            if (listBoxFreightMarketAddedJobs == null ||
                listBoxFreightMarketAddedJobs.SelectedItem == null)
            {
                ShowModernMarketMessageH13R(
                    "Selecione um trabalho criado nesta sessão para excluir.",
                    true);
                return;
            }

            DialogResult result = MessageBox.Show(
                "Excluir o trabalho selecionado da lista em memória?",
                "Excluir trabalho",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            try
            {
                FM_JobList_Delete();
                JobsAmountAdded =
                    AddedJobsList == null
                        ? 0
                        : AddedJobsList.Count;

                ShowModernMarketMessageH13R(
                    "Trabalho removido da lista em memória.",
                    false);
            }
            catch (Exception ex)
            {
                ShowModernMarketMessageH13R(
                    "Não foi possível excluir o trabalho: " +
                    ex.Message,
                    true);
            }
            finally
            {
                EnsureModernMarketControlsH13R();
            }
        }

        private void ModernFreightClearH13R(
            object sender,
            EventArgs e)
        {
            if (!EnsureModernMarketMutationAllowedH13R())
                return;

            if (AddedJobsList == null || AddedJobsList.Count == 0)
                return;

            DialogResult result = MessageBox.Show(
                "Remover todos os trabalhos preparados nesta sessão?",
                "Limpar lista de trabalhos",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            buttonClearJobList_Click(sender, e);
            FreightMarketJob = null;
            AttachModernMarketEventsH13R();
            EnsureModernMarketControlsH13R();
            ShowModernMarketMessageH13R(
                "Lista de trabalhos em memória limpa.",
                false);
        }

        private void ModernFreightCancelEditH13R(
            object sender,
            EventArgs e)
        {
            try
            {
                buttonFreightMarketCancelJobEdit_Click(sender, e);
                FreightMarketJob = null;
            }
            finally
            {
                AttachModernMarketEventsH13R();
                EnsureModernMarketControlsH13R();
            }
        }

        private Company FindModernUiCompanyH13R(
            string cityId,
            string companyId)
        {
            string normalizedCity =
                NormalizeModernMarketIdH13R(cityId);
            string normalizedCompany =
                NormalizeModernMarketIdH13R(companyId);

            City city = CitiesList == null
                ? null
                : CitiesList.Find(
                    item => item != null &&
                    String.Equals(
                        NormalizeModernMarketIdH13R(item.CityName),
                        normalizedCity,
                        StringComparison.OrdinalIgnoreCase));

            if (city == null)
                return null;

            List<Company> companies = city.ReturnCompanies();
            if (companies == null)
                return null;

            return companies.Find(
                company => company != null &&
                String.Equals(
                    NormalizeModernMarketIdH13R(company.CompanyName),
                    normalizedCompany,
                    StringComparison.OrdinalIgnoreCase));
        }

        private void CaptureModernCargoSeedUpdateH13R(
            string cityId,
            string companyId)
        {
            Company company =
                FindModernUiCompanyH13R(cityId, companyId);
            if (company == null)
                return;

            string key =
                BuildModernCompanyKeyH13R(
                    company.CompanyName,
                    cityId);
            if (String.IsNullOrWhiteSpace(key))
                return;

            uint[] seeds = company.CargoSeeds ?? new uint[0];
            uint[] copy = new uint[seeds.Length];
            Array.Copy(seeds, copy, seeds.Length);
            ModernCargoSeedUpdatesH13R[key] = copy;
        }

        private void CaptureModernCargoCityUpdatesH13R(
            string cityId)
        {
            string normalizedCity =
                NormalizeModernMarketIdH13R(cityId);
            City city = CitiesList == null
                ? null
                : CitiesList.Find(
                    item => item != null &&
                    String.Equals(
                        NormalizeModernMarketIdH13R(item.CityName),
                        normalizedCity,
                        StringComparison.OrdinalIgnoreCase));

            if (city == null)
                return;

            List<Company> companies = city.ReturnCompanies();
            if (companies == null)
                return;

            foreach (Company company in companies)
            {
                if (company == null || company.Excluded)
                    continue;

                CaptureModernCargoSeedUpdateH13R(
                    normalizedCity,
                    company.CompanyName);
            }
        }

        private void ModernCargoRandomizeCompanyH13R(
            object sender,
            EventArgs e)
        {
            if (!EnsureModernMarketMutationAllowedH13R())
                return;

            try
            {
                string cityId;
                string companyId;
                if (!TryGetModernCargoSelectionH13R(
                        true,
                        out cityId,
                        out companyId))
                {
                    return;
                }

                buttonCargoMarketRandomizeCargoCompany_Click(sender, e);
                CaptureModernCargoSeedUpdateH13R(
                    cityId,
                    companyId);
                ShowModernMarketMessageH13R(
                    "Ofertas da empresa randomizadas em memória. Clique em Salvar para gravar.",
                    false);
            }
            catch (Exception ex)
            {
                ShowModernMarketMessageH13R(
                    "Não foi possível randomizar as ofertas da empresa: " +
                    ex.Message,
                    true);
            }
        }

        private void ModernCargoResetCompanyH13R(
            object sender,
            EventArgs e)
        {
            if (!EnsureModernMarketMutationAllowedH13R())
                return;

            try
            {
                string cityId;
                string companyId;
                if (!TryGetModernCargoSelectionH13R(
                        true,
                        out cityId,
                        out companyId))
                {
                    return;
                }

                buttonCargoMarketResetCargoCompany_Click(sender, e);
                CaptureModernCargoSeedUpdateH13R(
                    cityId,
                    companyId);
                ShowModernMarketMessageH13R(
                    "Ofertas da empresa removidas em memória. Clique em Salvar para gravar.",
                    false);
            }
            catch (Exception ex)
            {
                ShowModernMarketMessageH13R(
                    "Não foi possível remover as ofertas da empresa: " +
                    ex.Message,
                    true);
            }
        }

        private void ModernCargoRandomizeCityH13R(
            object sender,
            EventArgs e)
        {
            if (!EnsureModernMarketMutationAllowedH13R())
                return;

            try
            {
                string cityId;
                string companyId;
                if (!TryGetModernCargoSelectionH13R(
                        false,
                        out cityId,
                        out companyId))
                {
                    return;
                }

                buttonCargoMarketRandomizeCargoCity_Click(sender, e);
                CaptureModernCargoCityUpdatesH13R(cityId);
                ShowModernMarketMessageH13R(
                    "Ofertas da cidade randomizadas em memória. Clique em Salvar para gravar.",
                    false);
            }
            catch (Exception ex)
            {
                ShowModernMarketMessageH13R(
                    "Não foi possível randomizar as ofertas da cidade: " +
                    ex.Message,
                    true);
            }
        }

        private void ModernCargoResetCityH13R(
            object sender,
            EventArgs e)
        {
            if (!EnsureModernMarketMutationAllowedH13R())
                return;

            try
            {
                string cityId;
                string companyId;
                if (!TryGetModernCargoSelectionH13R(
                        false,
                        out cityId,
                        out companyId))
                {
                    return;
                }

                buttonCargoMarketResetCargoCity_Click(sender, e);
                CaptureModernCargoCityUpdatesH13R(cityId);
                ShowModernMarketMessageH13R(
                    "Ofertas da cidade removidas em memória. Clique em Salvar para gravar.",
                    false);
            }
            catch (Exception ex)
            {
                ShowModernMarketMessageH13R(
                    "Não foi possível remover as ofertas da cidade: " +
                    ex.Message,
                    true);
            }
        }

        private void EnableModernMarketControlH13R(
            string name,
            bool enabled)
        {
            Control control =
                FindModernMarketControlH13R<Control>(name);
            if (control == null)
                return;

            control.Enabled = enabled;
            control.TabStop = enabled;

            TextBoxBase textBox = control as TextBoxBase;
            if (textBox != null)
                textBox.ReadOnly = !enabled;
        }

        private void EnsureModernMarketControlsH13R()
        {
            if (!IsModernCompatibilityMode())
                return;

            string[] alwaysEnabled =
            {
                "comboBoxFreightMarketCargoList",
                "comboBoxFreightMarketCountries",
                "comboBoxFreightMarketCompanies",
                "comboBoxFreightMarketUrgency",
                "comboBoxFreightMarketSourceCity",
                "comboBoxFreightMarketSourceCompany",
                "comboBoxFreightMarketDestinationCity",
                "comboBoxFreightMarketDestinationCompany",
                "comboBoxFreightMarketTrailerDef",
                "comboBoxFreightMarketTrailerVariant",
                "checkBoxFreightMarketFilterDestination",
                "checkBoxFreightMarketRandomDest",
                "buttonFreightMarketRandomizeCargo",
                "buttonFreightMarketAddJob",
                "listBoxFreightMarketAddedJobs"
            };

            foreach (string controlName in alwaysEnabled)
                EnableModernMarketControlH13R(controlName, true);

            TabPage cargoSelectionTab =
                FindModernMarketControlH13R<TabPage>(
                    "tabPageCargoMarket");
            EnableModernCargoSelectionControlsH13R(
                cargoSelectionTab);

            EnableModernMarketControlH13R(
                ModernCargoRandomCompanyButtonNameH13R,
                true);
            EnableModernMarketControlH13R(
                ModernCargoResetCompanyButtonNameH13R,
                true);
            EnableModernMarketControlH13R(
                ModernCargoRandomCityButtonNameH13R,
                true);
            EnableModernMarketControlH13R(
                ModernCargoResetCityButtonNameH13R,
                true);

            Button clear =
                FindModernMarketControlH13R<Button>(
                    "buttonFreightMarketClearJobList");
            if (clear != null)
            {
                clear.Enabled =
                    AddedJobsList != null &&
                    AddedJobsList.Count > 0;
            }

            TabPage freightTab =
                FindModernMarketControlH13R<TabPage>(
                    "tabPageFreightMarket");
            if (freightTab != null)
                freightTab.Enabled = true;

            TabPage cargoTab =
                FindModernMarketControlH13R<TabPage>(
                    "tabPageCargoMarket");
            if (cargoTab != null)
                cargoTab.Enabled = true;

            if (contextMenuStripFreightMarketJobListEdit != null)
                contextMenuStripFreightMarketJobListEdit.Enabled = true;
            if (contextMenuStripFreightMarketJobListDelete != null)
                contextMenuStripFreightMarketJobListDelete.Enabled = true;
        }

        private void ModernMarketUiActivationH13R(
            object sender,
            EventArgs e)
        {
            EnsureModernMarketControlsH13R();
            AttachModernMarketEventsH13R();
            RebuildModernFreightSourceCitiesH13R();
            ModernCargoCitySelectionDeterministicH13R(
                sender,
                e);
        }

        private void AttachModernMarketEventsH13R()
        {
            Button add =
                FindModernMarketControlH13R<Button>(
                    "buttonFreightMarketAddJob");
            if (add != null)
            {
                add.Click -= buttonAddJob_Click;
                add.Click -= buttonEditJob_Click;
                add.Click -= ModernFreightAddOrEditH13R;
                add.Click += ModernFreightAddOrEditH13R;
            }

            Button clear =
                FindModernMarketControlH13R<Button>(
                    "buttonFreightMarketClearJobList");
            if (clear != null)
            {
                clear.Click -= buttonClearJobList_Click;
                clear.Click -= ModernFreightClearH13R;
                clear.Click += ModernFreightClearH13R;
            }

            Button cancel =
                FindModernMarketControlH13R<Button>(
                    "buttonFreightMarketCancelJobEdit");
            if (cancel != null)
            {
                cancel.Click -= buttonFreightMarketCancelJobEdit_Click;
                cancel.Click -= ModernFreightCancelEditH13R;
                cancel.Click += ModernFreightCancelEditH13R;
            }

            if (contextMenuStripFreightMarketJobListEdit != null)
            {
                contextMenuStripFreightMarketJobListEdit.Click -=
                    contextMenuStripFreightMarketJobListEdit_Click;
                contextMenuStripFreightMarketJobListEdit.Click -=
                    ModernFreightEditCommandH13R;
                contextMenuStripFreightMarketJobListEdit.Click +=
                    ModernFreightEditCommandH13R;
                contextMenuStripFreightMarketJobListEdit.Enabled = true;
            }

            if (contextMenuStripFreightMarketJobListDelete != null)
            {
                contextMenuStripFreightMarketJobListDelete.Click -=
                    contextMenuStripFreightMarketJobListDelete_Click;
                contextMenuStripFreightMarketJobListDelete.Click -=
                    ModernFreightDeleteCommandH13R;
                contextMenuStripFreightMarketJobListDelete.Click +=
                    ModernFreightDeleteCommandH13R;
                contextMenuStripFreightMarketJobListDelete.Enabled = true;
            }

            Button randomCompany =
                FindModernMarketControlH13R<Button>(
                    ModernCargoRandomCompanyButtonNameH13R);
            if (randomCompany != null)
            {
                randomCompany.Click -=
                    buttonCargoMarketRandomizeCargoCompany_Click;
                randomCompany.Click -=
                    ModernCargoRandomizeCompanyH13R;
                randomCompany.Click +=
                    ModernCargoRandomizeCompanyH13R;
            }

            Button resetCompany =
                FindModernMarketControlH13R<Button>(
                    ModernCargoResetCompanyButtonNameH13R);
            if (resetCompany != null)
            {
                resetCompany.Click -=
                    buttonCargoMarketResetCargoCompany_Click;
                resetCompany.Click -=
                    ModernCargoResetCompanyH13R;
                resetCompany.Click +=
                    ModernCargoResetCompanyH13R;
            }

            Button randomCity =
                FindModernMarketControlH13R<Button>(
                    ModernCargoRandomCityButtonNameH13R);
            if (randomCity != null)
            {
                randomCity.Click -=
                    buttonCargoMarketRandomizeCargoCity_Click;
                randomCity.Click -=
                    ModernCargoRandomizeCityH13R;
                randomCity.Click +=
                    ModernCargoRandomizeCityH13R;
            }

            Button resetCity =
                FindModernMarketControlH13R<Button>(
                    ModernCargoResetCityButtonNameH13R);
            if (resetCity != null)
            {
                resetCity.Click -=
                    buttonCargoMarketResetCargoCity_Click;
                resetCity.Click -=
                    ModernCargoResetCityH13R;
                resetCity.Click +=
                    ModernCargoResetCityH13R;
            }

            if (!ModernMarketEventsAttachedH13R)
            {
                if (comboBoxFreightMarketSourceCity != null)
                {
                    comboBoxFreightMarketSourceCity.SelectedIndexChanged -=
                        ModernFreightSourceCityChangedH13R;
                    comboBoxFreightMarketSourceCity.SelectedIndexChanged +=
                        ModernFreightSourceCityChangedH13R;
                }

                ComboBox cargoCity = FindModernCargoComboH13R("City");
                if (cargoCity != null)
                {
                    cargoCity.SelectedIndexChanged -=
                        ModernCargoCitySelectionDeterministicH13R;
                    cargoCity.SelectedIndexChanged +=
                        ModernCargoCitySelectionDeterministicH13R;
                }

                ComboBox cargoCompany = FindModernCargoComboH13R("Company");
                if (cargoCompany != null)
                {
                    cargoCompany.SelectionChangeCommitted -=
                        ModernCargoCompanySelectionRememberH13R;
                    cargoCompany.SelectionChangeCommitted +=
                        ModernCargoCompanySelectionRememberH13R;
                }

                TabPage freightTab =
                    FindModernMarketControlH13R<TabPage>(
                        "tabPageFreightMarket");
                if (freightTab != null)
                {
                    freightTab.Enter -= ModernMarketUiActivationH13R;
                    freightTab.Enter += ModernMarketUiActivationH13R;
                }

                TabPage cargoTab =
                    FindModernMarketControlH13R<TabPage>(
                        "tabPageCargoMarket");
                if (cargoTab != null)
                {
                    cargoTab.Enter -= ModernMarketUiActivationH13R;
                    cargoTab.Enter += ModernMarketUiActivationH13R;
                }

                tabControlMain.SelectedIndexChanged -=
                    ModernMarketUiActivationH13R;
                tabControlMain.SelectedIndexChanged +=
                    ModernMarketUiActivationH13R;

                ModernMarketEventsAttachedH13R = true;
            }
        }

        private bool TryReadModernBlockHeaderH13R(
            string trimmed,
            out string blockType,
            out string blockId)
        {
            blockType = "";
            blockId = "";

            if (String.IsNullOrWhiteSpace(trimmed) ||
                !trimmed.EndsWith("{", StringComparison.Ordinal))
            {
                return false;
            }

            int colon = trimmed.IndexOf(':');
            if (colon <= 0)
                return false;

            blockType = trimmed.Substring(0, colon).Trim();
            blockId = trimmed.Substring(
                colon + 1,
                trimmed.Length - colon - 2).Trim();

            return !String.IsNullOrWhiteSpace(blockType) &&
                   !String.IsNullOrWhiteSpace(blockId);
        }

        private string ReadModernTagH13R(
            string trimmed,
            out string value)
        {
            value = "";
            if (String.IsNullOrWhiteSpace(trimmed))
                return "";

            int colon = trimmed.IndexOf(':');
            if (colon <= 0)
                return "";

            value = trimmed.Substring(colon + 1).Trim();
            return trimmed.Substring(0, colon).Trim();
        }

        private void ParseModernMarketTopologyH13R(
            string input,
            out Dictionary<string, List<string>> companyOffers,
            out Dictionary<string, bool> companyDiscovered,
            out HashSet<string> jobOfferBlockIds,
            out Dictionary<string, ModernMarketEventInfoH13R> events,
            out List<string> eventQueue)
        {
            companyOffers =
                new Dictionary<string, List<string>>(
                    StringComparer.OrdinalIgnoreCase);
            companyDiscovered =
                new Dictionary<string, bool>(
                    StringComparer.OrdinalIgnoreCase);
            jobOfferBlockIds =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            events =
                new Dictionary<string, ModernMarketEventInfoH13R>(
                    StringComparer.OrdinalIgnoreCase);
            eventQueue = new List<string>();

            string[] lines = input.Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n');

            string blockType = "";
            string blockId = "";
            List<KeyValuePair<int, string>> indexedValues =
                new List<KeyValuePair<int, string>>();
            ModernMarketEventInfoH13R currentEvent = null;

            foreach (string rawLine in lines)
            {
                string trimmed = (rawLine ?? "").Trim();

                if (String.IsNullOrWhiteSpace(blockType))
                {
                    if (TryReadModernBlockHeaderH13R(
                            trimmed,
                            out blockType,
                            out blockId))
                    {
                        indexedValues.Clear();
                        if (String.Equals(
                                blockType,
                                "company",
                                StringComparison.Ordinal))
                        {
                            companyDiscovered[blockId] = false;
                        }
                        else if (String.Equals(
                                     blockType,
                                     "job_offer_data",
                                     StringComparison.Ordinal))
                        {
                            jobOfferBlockIds.Add(blockId);
                        }

                        currentEvent =
                            String.Equals(
                                blockType,
                                "economy_event",
                                StringComparison.Ordinal)
                                ? new ModernMarketEventInfoH13R
                                  {
                                      Id = blockId,
                                      Time = 0,
                                      UnitLink = "",
                                      Param = -1,
                                      OriginalQueueIndex = -1
                                  }
                                : null;
                    }

                    continue;
                }

                if (trimmed == "}")
                {
                    if (String.Equals(
                            blockType,
                            "company",
                            StringComparison.Ordinal))
                    {
                        indexedValues.Sort(
                            delegate(
                                KeyValuePair<int, string> left,
                                KeyValuePair<int, string> right)
                            {
                                return left.Key.CompareTo(right.Key);
                            });

                        List<string> offers = new List<string>();
                        if (indexedValues.Count > 0)
                        {
                            int highestIndex =
                                indexedValues[indexedValues.Count - 1].Key;
                            for (int index = 0;
                                 index <= highestIndex;
                                 index++)
                            {
                                offers.Add("");
                            }

                            foreach (KeyValuePair<int, string> item
                                     in indexedValues)
                            {
                                offers[item.Key] = item.Value;
                            }
                        }

                        companyOffers[blockId] = offers;
                    }
                    else if (String.Equals(
                                 blockType,
                                 "economy_event_queue",
                                 StringComparison.Ordinal))
                    {
                        indexedValues.Sort(
                            delegate(
                                KeyValuePair<int, string> left,
                                KeyValuePair<int, string> right)
                            {
                                return left.Key.CompareTo(right.Key);
                            });

                        eventQueue = new List<string>();
                        foreach (KeyValuePair<int, string> item
                                 in indexedValues)
                        {
                            eventQueue.Add(item.Value);
                        }
                    }
                    else if (currentEvent != null)
                    {
                        events[currentEvent.Id] = currentEvent;
                    }

                    blockType = "";
                    blockId = "";
                    currentEvent = null;
                    indexedValues.Clear();
                    continue;
                }

                string value;
                string tag = ReadModernTagH13R(trimmed, out value);
                if (String.IsNullOrWhiteSpace(tag))
                    continue;

                if (String.Equals(
                        blockType,
                        "company",
                        StringComparison.Ordinal) &&
                    tag.StartsWith(
                        "job_offer[",
                        StringComparison.Ordinal))
                {
                    int open = tag.IndexOf('[');
                    int close = tag.IndexOf(']');
                    int index;
                    if (open >= 0 && close > open &&
                        Int32.TryParse(
                            tag.Substring(open + 1, close - open - 1),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out index))
                    {
                        indexedValues.Add(
                            new KeyValuePair<int, string>(
                                index,
                                NormalizeModernMarketIdH13R(value)));
                    }
                }
                else if (String.Equals(
                             blockType,
                             "economy_event_queue",
                             StringComparison.Ordinal) &&
                         tag.StartsWith(
                             "data[",
                             StringComparison.Ordinal))
                {
                    int open = tag.IndexOf('[');
                    int close = tag.IndexOf(']');
                    int index;
                    if (open >= 0 && close > open &&
                        Int32.TryParse(
                            tag.Substring(open + 1, close - open - 1),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out index))
                    {
                        indexedValues.Add(
                            new KeyValuePair<int, string>(
                                index,
                                NormalizeModernMarketIdH13R(value)));
                    }
                }
                else if (String.Equals(
                             blockType,
                             "company",
                             StringComparison.Ordinal) &&
                         tag == "discovered")
                {
                    bool discovered;
                    if (Boolean.TryParse(value, out discovered))
                        companyDiscovered[blockId] = discovered;
                }
                else if (currentEvent != null)
                {
                    if (tag == "time")
                    {
                        uint parsed;
                        if (UInt32.TryParse(
                                value,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out parsed))
                        {
                            currentEvent.Time = parsed;
                        }
                    }
                    else if (tag == "unit_link")
                    {
                        currentEvent.UnitLink =
                            NormalizeModernMarketIdH13R(value);
                    }
                    else if (tag == "param")
                    {
                        int parsed;
                        if (Int32.TryParse(
                                value,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out parsed))
                        {
                            currentEvent.Param = parsed;
                        }
                    }
                }
            }

            for (int index = 0; index < eventQueue.Count; index++)
            {
                ModernMarketEventInfoH13R info;
                if (events.TryGetValue(eventQueue[index], out info))
                    info.OriginalQueueIndex = index;
            }
        }

        private string BuildModernJobFieldH13R(
            string tag,
            JobAdded job)
        {
            if (tag == "target")
            {
                return "\"" +
                       NormalizeModernMarketIdH13R(
                           job.DestinationCompany) + "." +
                       NormalizeModernMarketIdH13R(
                           job.DestinationCity) + "\"";
            }

            if (tag == "expiration_time")
                return job.ExpirationTime.ToString(CultureInfo.InvariantCulture);
            if (tag == "urgency")
                return job.Urgency.ToString(CultureInfo.InvariantCulture);
            if (tag == "shortest_distance_km")
                return job.Distance.ToString(CultureInfo.InvariantCulture);
            if (tag == "ferry_time")
                return job.Ferrytime.ToString(CultureInfo.InvariantCulture);
            if (tag == "ferry_price")
                return job.Ferryprice.ToString(CultureInfo.InvariantCulture);
            if (tag == "cargo")
                return "cargo." + NormalizeModernMarketIdH13R(job.Cargo);
            if (tag == "company_truck")
                return NormalizeModernMarketIdH13R(job.CompanyTruck);
            if (tag == "trailer_variant")
                return NormalizeModernMarketIdH13R(job.TrailerVariant);
            if (tag == "trailer_definition")
                return NormalizeModernMarketIdH13R(job.TrailerDefinition);
            if (tag == "units_count")
                return job.UnitsCount.ToString(CultureInfo.InvariantCulture);

            return null;
        }

        private string PatchModernMarketDataH13R(string input)
        {
            if (String.IsNullOrWhiteSpace(input) ||
                !HasUnsavedModernMarketChangesH13R())
            {
                return input;
            }

            string reason;
            if (!CanModifyModernMarketsH13R(out reason))
            {
                throw new InvalidOperationException(reason);
            }

            Dictionary<string, List<string>> companyOffers;
            Dictionary<string, bool> companyDiscovered;
            HashSet<string> jobOfferBlockIds;
            Dictionary<string, ModernMarketEventInfoH13R> events;
            List<string> eventQueue;
            ParseModernMarketTopologyH13R(
                input,
                out companyOffers,
                out companyDiscovered,
                out jobOfferBlockIds,
                out events,
                out eventQueue);

            Dictionary<string, JobAdded> jobsByOfferId =
                new Dictionary<string, JobAdded>(
                    StringComparer.OrdinalIgnoreCase);
            Dictionary<string, uint> eventTimeUpdates =
                new Dictionary<string, uint>(
                    StringComparer.OrdinalIgnoreCase);

            List<string> freightAuditLines = new List<string>();

            foreach (KeyValuePair<string, List<JobAdded>> pair
                     in AddedJobsDictionary)
            {
                List<string> offerIds;
                if (!companyOffers.TryGetValue(
                        pair.Key,
                        out offerIds))
                {
                    throw new InvalidOperationException(
                        "Empresa de origem não encontrada no save: " +
                        pair.Key);
                }

                bool discovered;
                if (!companyDiscovered.TryGetValue(
                        pair.Key,
                        out discovered) ||
                    !discovered)
                {
                    throw new InvalidOperationException(
                        "A empresa de origem não está descoberta no save: " +
                        pair.Key + ".");
                }

                List<ModernMarketOfferSlotH13R> usableSlots =
                    new List<ModernMarketOfferSlotH13R>();
                for (int slotIndex = 0;
                     slotIndex < offerIds.Count;
                     slotIndex++)
                {
                    string offerId =
                        NormalizeModernMarketIdH13R(
                            offerIds[slotIndex]);
                    if (String.IsNullOrWhiteSpace(offerId) ||
                        !jobOfferBlockIds.Contains(offerId))
                    {
                        continue;
                    }

                    ModernMarketEventInfoH13R matchingEvent = null;
                    foreach (ModernMarketEventInfoH13R candidate
                             in events.Values)
                    {
                        if (candidate.Param == slotIndex &&
                            candidate.OriginalQueueIndex >= 0 &&
                            String.Equals(
                                candidate.UnitLink,
                                pair.Key,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            if (matchingEvent != null)
                            {
                                throw new InvalidOperationException(
                                    "Mais de um evento ativo foi encontrado para " +
                                    pair.Key + " / vaga " +
                                    slotIndex.ToString(
                                        CultureInfo.InvariantCulture) +
                                    ".");
                            }

                            matchingEvent = candidate;
                        }
                    }

                    if (matchingEvent != null)
                    {
                        usableSlots.Add(
                            new ModernMarketOfferSlotH13R
                            {
                                OfferId = offerId,
                                SlotIndex = slotIndex,
                                EventId = matchingEvent.Id
                            });
                    }
                }

                if (pair.Value.Count > usableSlots.Count)
                {
                    throw new InvalidOperationException(
                        "A empresa " + pair.Key + " possui apenas " +
                        usableSlots.Count.ToString(
                            CultureInfo.InvariantCulture) +
                        " vaga(s) real(is) de oferta, mas foram preparados " +
                        pair.Value.Count.ToString(
                            CultureInfo.InvariantCulture) +
                        " trabalho(s)." );
                }

                for (int index = 0;
                     index < pair.Value.Count;
                     index++)
                {
                    JobAdded job = pair.Value[index];
                    string sourceKey =
                        BuildModernCompanyKeyH13R(
                            job.SourceCompany,
                            job.SourceCity);
                    string destinationKey =
                        BuildModernCompanyKeyH13R(
                            job.DestinationCompany,
                            job.DestinationCity);

                    if (!String.Equals(
                            sourceKey,
                            pair.Key,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "A origem do trabalho não corresponde à empresa " +
                            "que reservou a oferta: " + pair.Key + ".");
                    }

                    if (!companyOffers.ContainsKey(destinationKey))
                    {
                        throw new InvalidOperationException(
                            "Empresa de destino não encontrada no save: " +
                            destinationKey + ".");
                    }

                    if (String.Equals(
                            sourceKey,
                            destinationKey,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "Origem e destino são a mesma empresa: " +
                            sourceKey + ".");
                    }

                    if (!job.realDistance ||
                        job.Distance <= 1 ||
                        job.Distance == 11111)
                    {
                        throw new InvalidOperationException(
                            "O trabalho de " + sourceKey + " para " +
                            destinationKey +
                            " não possui distância real válida.");
                    }

                    if (job.ExpirationTime <=
                        (uint)SiiNunitData.Economy.game_time)
                    {
                        throw new InvalidOperationException(
                            "O trabalho de " + sourceKey + " para " +
                            destinationKey + " já estaria expirado.");
                    }

                    if (String.IsNullOrWhiteSpace(
                            NormalizeModernMarketIdH13R(job.Cargo)) ||
                        String.IsNullOrWhiteSpace(
                            NormalizeModernMarketIdH13R(job.CompanyTruck)) ||
                        String.IsNullOrWhiteSpace(
                            NormalizeModernMarketIdH13R(job.TrailerVariant)) ||
                        String.IsNullOrWhiteSpace(
                            NormalizeModernMarketIdH13R(job.TrailerDefinition)) ||
                        job.UnitsCount <= 0)
                    {
                        throw new InvalidOperationException(
                            "O trabalho de " + sourceKey + " para " +
                            destinationKey +
                            " possui carga ou configuração de veículo incompleta.");
                    }

                    ModernMarketOfferSlotH13R slot =
                        usableSlots[index];
                    jobsByOfferId[slot.OfferId] = job;

                    ModernMarketEventInfoH13R matchingEvent =
                        events[slot.EventId];
                    eventTimeUpdates[matchingEvent.Id] =
                        job.ExpirationTime;
                    matchingEvent.Time = job.ExpirationTime;

                    freightAuditLines.Add(
                        "RC5.1H13R.8.2 freight patch: source=" +
                        sourceKey +
                        ", target=" + destinationKey +
                        ", offer=" + slot.OfferId +
                        ", slot=" +
                        slot.SlotIndex.ToString(
                            CultureInfo.InvariantCulture) +
                        ", event=" + matchingEvent.Id +
                        ", distance=" +
                        job.Distance.ToString(
                            CultureInfo.InvariantCulture) +
                        ", cargo=" +
                        NormalizeModernMarketIdH13R(job.Cargo) + ".");
                }
            }

            List<string> sortedQueue =
                new List<string>(eventQueue);
            if (jobsByOfferId.Count > 0)
            {
                if (sortedQueue.Count == 0)
                {
                    throw new InvalidOperationException(
                        "A fila de eventos econômicos não foi encontrada no save.");
                }

                sortedQueue.Sort(
                    delegate(string left, string right)
                    {
                        ModernMarketEventInfoH13R leftInfo;
                        ModernMarketEventInfoH13R rightInfo;
                        if (!events.TryGetValue(left, out leftInfo) ||
                            !events.TryGetValue(right, out rightInfo))
                        {
                            return 0;
                        }

                        int compare = leftInfo.Time.CompareTo(rightInfo.Time);
                        if (compare != 0)
                            return compare;

                        return leftInfo.OriginalQueueIndex.CompareTo(
                            rightInfo.OriginalQueueIndex);
                    });
            }

            string[] inputLines = input.Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n');
            List<string> output =
                new List<string>(inputLines.Length + 32);

            string blockType = "";
            string blockId = "";
            bool skipCargoSeedItems = false;
            bool skipEventQueueItems = false;
            HashSet<string> patchedCargoCompanies =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> patchedEventTimes =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> patchedQueue =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, HashSet<string>> patchedJobFields =
                new Dictionary<string, HashSet<string>>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in inputLines)
            {
                string line = rawLine ?? "";
                string trimmed = line.Trim();

                if (String.IsNullOrWhiteSpace(blockType))
                {
                    TryReadModernBlockHeaderH13R(
                        trimmed,
                        out blockType,
                        out blockId);
                    output.Add(line);
                    continue;
                }

                if (trimmed == "}")
                {
                    blockType = "";
                    blockId = "";
                    skipCargoSeedItems = false;
                    skipEventQueueItems = false;
                    output.Add(line);
                    continue;
                }

                string value;
                string tag = ReadModernTagH13R(trimmed, out value);
                if (String.IsNullOrWhiteSpace(tag))
                {
                    output.Add(line);
                    continue;
                }

                if (String.Equals(
                        blockType,
                        "company",
                        StringComparison.Ordinal) &&
                    ModernCargoSeedUpdatesH13R.ContainsKey(blockId))
                {
                    if (tag == "cargo_offer_seeds")
                    {
                        uint[] seeds =
                            ModernCargoSeedUpdatesH13R[blockId] ??
                            new uint[0];
                        string indent = GetLeadingWhitespaceH13R(line);
                        output.Add(
                            indent + "cargo_offer_seeds: " +
                            seeds.Length.ToString(
                                CultureInfo.InvariantCulture));

                        for (int index = 0;
                             index < seeds.Length;
                             index++)
                        {
                            output.Add(
                                indent + "cargo_offer_seeds[" +
                                index.ToString(
                                    CultureInfo.InvariantCulture) +
                                "]: " +
                                seeds[index].ToString(
                                    CultureInfo.InvariantCulture));
                        }

                        skipCargoSeedItems = true;
                        patchedCargoCompanies.Add(blockId);
                        continue;
                    }

                    if (skipCargoSeedItems &&
                        tag.StartsWith(
                            "cargo_offer_seeds[",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    skipCargoSeedItems = false;
                }

                if (String.Equals(
                        blockType,
                        "job_offer_data",
                        StringComparison.Ordinal) &&
                    jobsByOfferId.ContainsKey(blockId))
                {
                    string replacement =
                        BuildModernJobFieldH13R(
                            tag,
                            jobsByOfferId[blockId]);
                    if (replacement != null)
                    {
                        output.Add(
                            GetLeadingWhitespaceH13R(line) +
                            tag + ": " + replacement);

                        HashSet<string> fields;
                        if (!patchedJobFields.TryGetValue(
                                blockId,
                                out fields))
                        {
                            fields = new HashSet<string>(
                                StringComparer.Ordinal);
                            patchedJobFields[blockId] = fields;
                        }

                        fields.Add(tag);
                        continue;
                    }
                }

                if (String.Equals(
                        blockType,
                        "economy_event",
                        StringComparison.Ordinal) &&
                    tag == "time" &&
                    eventTimeUpdates.ContainsKey(blockId))
                {
                    output.Add(
                        GetLeadingWhitespaceH13R(line) +
                        "time: " +
                        eventTimeUpdates[blockId].ToString(
                            CultureInfo.InvariantCulture));
                    patchedEventTimes.Add(blockId);
                    continue;
                }

                if (String.Equals(
                        blockType,
                        "economy_event_queue",
                        StringComparison.Ordinal) &&
                    jobsByOfferId.Count > 0)
                {
                    if (tag == "data")
                    {
                        string indent = GetLeadingWhitespaceH13R(line);
                        output.Add(
                            indent + "data: " +
                            sortedQueue.Count.ToString(
                                CultureInfo.InvariantCulture));

                        for (int index = 0;
                             index < sortedQueue.Count;
                             index++)
                        {
                            output.Add(
                                indent + "data[" +
                                index.ToString(
                                    CultureInfo.InvariantCulture) +
                                "]: " + sortedQueue[index]);
                        }

                        skipEventQueueItems = true;
                        patchedQueue.Add(blockId);
                        continue;
                    }

                    if (skipEventQueueItems &&
                        tag.StartsWith(
                            "data[",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    skipEventQueueItems = false;
                }

                output.Add(line);
            }

            foreach (string companyKey
                     in ModernCargoSeedUpdatesH13R.Keys)
            {
                if (!patchedCargoCompanies.Contains(companyKey))
                {
                    throw new InvalidOperationException(
                        "Não foi possível localizar cargo_offer_seeds em " +
                        companyKey + ".");
                }
            }

            string[] requiredJobFields =
            {
                "target",
                "expiration_time",
                "urgency",
                "shortest_distance_km",
                "ferry_time",
                "ferry_price",
                "cargo",
                "company_truck",
                "trailer_variant",
                "trailer_definition",
                "units_count"
            };

            foreach (string offerId in jobsByOfferId.Keys)
            {
                HashSet<string> fields;
                if (!patchedJobFields.TryGetValue(offerId, out fields))
                {
                    throw new InvalidOperationException(
                        "Bloco job_offer_data não encontrado: " +
                        offerId);
                }

                foreach (string required in requiredJobFields)
                {
                    if (!fields.Contains(required))
                    {
                        throw new InvalidOperationException(
                            "Campo " + required +
                            " não encontrado no trabalho " +
                            offerId + ".");
                    }
                }
            }

            foreach (string eventId in eventTimeUpdates.Keys)
            {
                if (!patchedEventTimes.Contains(eventId))
                {
                    throw new InvalidOperationException(
                        "Não foi possível atualizar o evento " +
                        eventId + ".");
                }
            }

            if (jobsByOfferId.Count > 0 &&
                patchedQueue.Count != 1)
            {
                throw new InvalidOperationException(
                    "A fila de eventos econômicos deveria ser atualizada exatamente uma vez.");
            }

            foreach (string auditLine in freightAuditLines)
                IO_Utilities.LogWriter(auditLine);

            IO_Utilities.LogWriter(
                "RC5.1H13R.8.2 modern markets patched: freight_jobs=" +
                jobsByOfferId.Count.ToString(CultureInfo.InvariantCulture) +
                ", cargo_companies=" +
                patchedCargoCompanies.Count.ToString(
                    CultureInfo.InvariantCulture) + ".");

            return String.Join(Environment.NewLine, output);
        }

        private void InitializeModernMarketsH13R()
        {
            if (!IsModernCompatibilityMode())
                return;

            bool newDataIdentity =
                !Object.ReferenceEquals(
                    ModernMarketDataIdentityH13R,
                    SiiNunitData);
            if (newDataIdentity)
            {
                ModernMarketDataIdentityH13R = SiiNunitData;
                ModernCargoSeedUpdatesH13R.Clear();
                FreightMarketJob = null;
            }

            AttachModernMarketEventsH13R();
            RebuildModernFreightSourceCitiesH13R();

            ModernCargoCitySelectionDeterministicH13R(
                this,
                EventArgs.Empty);
            EnsureModernMarketControlsH13R();

            IO_Utilities.LogWriter(
                "RC5.1H13R.8.2 resilient Freight Market selection binding, semantic validation, discovered-source filtering and deterministic Cargo Market selection initialized.");
        }
    }
}