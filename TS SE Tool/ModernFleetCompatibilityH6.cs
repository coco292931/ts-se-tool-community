using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

using TS_SE_Tool.Save.Items;
using TS_SE_Tool.Utilities;

namespace TS_SE_Tool
{
    public partial class FormMain
    {
        // H6 expands the already homologated surgical writer to maintenance
        // fields of every owned truck and every owned main trailer.
        private readonly Dictionary<string, ModernVehicleSnapshot>
            ModernOwnedTruckBaselinesH6 =
                new Dictionary<string, ModernVehicleSnapshot>(
                    StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ModernVehicleSnapshot>
            ModernOwnedTrailerBaselinesH6 =
                new Dictionary<string, ModernVehicleSnapshot>(
                    StringComparer.OrdinalIgnoreCase);

        private bool ModernFleetBaselinesReadyH6 = false;
        private string ModernFleetBaselineSavePathH6 = "";
        private DateTime ModernFleetBaselineTimestampH6 = DateTime.MinValue;

        private bool ModernReferenceListContainsH6(
            IEnumerable<string> values,
            string requestedId)
        {
            string normalizedRequested =
                NormalizeModernReference(requestedId);

            if (values == null ||
                String.IsNullOrWhiteSpace(normalizedRequested))
            {
                return false;
            }

            return values.Any(value =>
                NormalizeModernReference(value).Equals(
                    normalizedRequested,
                    StringComparison.OrdinalIgnoreCase));
        }

        private bool IsOwnedModernTruckH6(string truckId)
        {
            string normalized = NormalizeModernReference(truckId);

            return !String.IsNullOrWhiteSpace(normalized) &&
                   SiiNunitData != null &&
                   SiiNunitData.Player != null &&
                   ModernReferenceListContainsH6(
                       SiiNunitData.Player.trucks,
                       normalized) &&
                   UserTruckDictionary != null &&
                   UserTruckDictionary.ContainsKey(normalized) &&
                   IsModernTruckObject(normalized);
        }

        private bool IsOwnedModernTrailerH6(string trailerId)
        {
            string normalized = NormalizeModernReference(trailerId);

            return !String.IsNullOrWhiteSpace(normalized) &&
                   SiiNunitData != null &&
                   SiiNunitData.Player != null &&
                   ModernReferenceListContainsH6(
                       SiiNunitData.Player.trailers,
                       normalized) &&
                   UserTrailerDictionary != null &&
                   UserTrailerDictionary.ContainsKey(normalized) &&
                   IsModernTrailerObject(normalized);
        }

        private bool CanEditModernTruckDataH6(string truckId)
        {
            string normalized = NormalizeModernReference(truckId);

            return IsOwnedModernTruckH6(normalized) &&
                   ModernOwnedTruckBaselinesH6.ContainsKey(normalized) &&
                   String.IsNullOrWhiteSpace(
                       ModernPendingTruckSwitchTargetTruckId);
        }

        private bool CanEditModernTrailerDataH6(string trailerId)
        {
            string normalized = NormalizeModernReference(trailerId);

            bool activeTrailer =
                !String.IsNullOrWhiteSpace(normalized) &&
                normalized.Equals(
                    ModernActiveTrailerId,
                    StringComparison.OrdinalIgnoreCase);

            return (activeTrailer ||
                    (IsOwnedModernTrailerH6(normalized) &&
                     ModernOwnedTrailerBaselinesH6.ContainsKey(normalized))) &&
                   String.IsNullOrWhiteSpace(
                       ModernPendingTruckSwitchTargetTruckId) &&
                   IsModernTrailerObject(normalized);
        }

        private string GetSelectedModernTruckIdH6()
        {
            return GetSelectedModernTruckId();
        }

        private string GetSelectedModernTrailerIdH6()
        {
            return GetSelectedModernTrailerIdH2();
        }

        private bool ModernFleetBaselineIsCurrentH6()
        {
            return ModernFleetBaselinesReadyH6 &&
                   String.Equals(
                       ModernFleetBaselineSavePathH6,
                       Globals.SelectedSavePath ?? "",
                       StringComparison.OrdinalIgnoreCase) &&
                   ModernFleetBaselineTimestampH6 ==
                       LastModifiedTimestamp;
        }

        private void CaptureModernFleetBaselinesH6()
        {
            ModernOwnedTruckBaselinesH6.Clear();
            ModernOwnedTrailerBaselinesH6.Clear();

            if (SiiNunitData == null ||
                SiiNunitData.Player == null)
            {
                ModernFleetBaselinesReadyH6 = false;
                return;
            }

            foreach (string rawTruckId in
                     SiiNunitData.Player.trucks ??
                     new List<string>())
            {
                string truckId =
                    NormalizeModernReference(rawTruckId);

                if (!IsOwnedModernTruckH6(truckId))
                    continue;

                ModernOwnedTruckBaselinesH6[truckId] =
                    CaptureModernTruckSnapshot(
                        (Vehicle)SiiNunitData.SiiNitems[truckId]);
            }

            foreach (string rawTrailerId in
                     SiiNunitData.Player.trailers ??
                     new List<string>())
            {
                string trailerId =
                    NormalizeModernReference(rawTrailerId);

                if (!IsOwnedModernTrailerH6(trailerId))
                    continue;

                ModernOwnedTrailerBaselinesH6[trailerId] =
                    CaptureModernTrailerSnapshot(
                        (Trailer)SiiNunitData.SiiNitems[trailerId]);
            }

            ModernFleetBaselineSavePathH6 =
                Globals.SelectedSavePath ?? "";
            ModernFleetBaselineTimestampH6 =
                LastModifiedTimestamp;
            ModernFleetBaselinesReadyH6 = true;

            IO_Utilities.LogWriter(
                "RC5.1H6 fleet baselines captured: trucks=" +
                ModernOwnedTruckBaselinesH6.Count.ToString(
                    CultureInfo.InvariantCulture) +
                ", trailers=" +
                ModernOwnedTrailerBaselinesH6.Count.ToString(
                    CultureInfo.InvariantCulture));
        }

        private string PatchModernFleetVehicleDataH6(string content)
        {
            if (!ModernFleetBaselineIsCurrentH6())
                return content;

            string output = content;

            foreach (KeyValuePair<string, ModernVehicleSnapshot> item in
                     ModernOwnedTruckBaselinesH6)
            {
                string truckId = item.Key;

                if (truckId.Equals(
                        ModernActiveTruckId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !IsModernTruckObject(truckId))
                {
                    continue;
                }

                ModernVehicleSnapshot current =
                    CaptureModernTruckSnapshot(
                        (Vehicle)SiiNunitData.SiiNitems[truckId]);

                output = PatchModernUnitFields(
                    output,
                    "vehicle",
                    truckId,
                    GetChangedModernScalars(item.Value, current),
                    GetChangedModernArrays(item.Value, current));
            }

            foreach (KeyValuePair<string, ModernVehicleSnapshot> item in
                     ModernOwnedTrailerBaselinesH6)
            {
                string trailerId = item.Key;

                if (ModernTrailerChainIds.Contains(trailerId) ||
                    !IsModernTrailerObject(trailerId))
                {
                    continue;
                }

                ModernVehicleSnapshot current =
                    CaptureModernTrailerSnapshot(
                        (Trailer)SiiNunitData.SiiNitems[trailerId]);

                output = PatchModernUnitFields(
                    output,
                    "trailer",
                    trailerId,
                    GetChangedModernScalars(item.Value, current),
                    GetChangedModernArrays(item.Value, current));
            }

            return output;
        }

        private bool HasUnsavedModernFleetChangesH6()
        {
            if (!ModernFleetBaselineIsCurrentH6())
                return false;

            foreach (KeyValuePair<string, ModernVehicleSnapshot> item in
                     ModernOwnedTruckBaselinesH6)
            {
                if (item.Key.Equals(
                        ModernActiveTruckId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !IsModernTruckObject(item.Key))
                {
                    continue;
                }

                ModernVehicleSnapshot current =
                    CaptureModernTruckSnapshot(
                        (Vehicle)SiiNunitData.SiiNitems[item.Key]);

                if (GetChangedModernScalars(
                        item.Value,
                        current).Count > 0 ||
                    GetChangedModernArrays(
                        item.Value,
                        current).Count > 0)
                {
                    return true;
                }
            }

            foreach (KeyValuePair<string, ModernVehicleSnapshot> item in
                     ModernOwnedTrailerBaselinesH6)
            {
                if (ModernTrailerChainIds.Contains(item.Key) ||
                    !IsModernTrailerObject(item.Key))
                {
                    continue;
                }

                ModernVehicleSnapshot current =
                    CaptureModernTrailerSnapshot(
                        (Trailer)SiiNunitData.SiiNitems[item.Key]);

                if (GetChangedModernScalars(
                        item.Value,
                        current).Count > 0 ||
                    GetChangedModernArrays(
                        item.Value,
                        current).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshModernSelectedTruckAdvancedH6(
            string truckId,
            bool allowEditing)
        {
            if (ModernTruckOdometerControl == null)
                return;

            bool available =
                allowEditing &&
                IsModernTruckObject(truckId) &&
                ModernUnitContainsField(
                    "vehicle",
                    truckId,
                    "odometer") &&
                ModernUnitContainsField(
                    "vehicle",
                    truckId,
                    "odometer_float_part");

            ModernAdvancedValuesLoading = true;

            try
            {
                ModernTruckOdometerControl.Enabled = available;

                if (available)
                {
                    Vehicle truckData =
                        (Vehicle)SiiNunitData.SiiNitems[truckId];

                    ModernTruckOdometerControl.Value =
                        ClampModernNumericValue(
                            ModernTruckOdometerControl,
                            Decimal.Round(
                                ModernCombinedOdometer(
                                    truckData.odometer,
                                    truckData.odometer_float_part),
                                ModernTruckOdometerControl.DecimalPlaces));
                }
            }
            finally
            {
                ModernAdvancedValuesLoading = false;
            }
        }

        private void RefreshModernSelectedTrailerAdvancedH6(
            string trailerId,
            bool allowEditing)
        {
            if (ModernTrailerOdometerControl == null ||
                ModernTrailerCargoMassControl == null)
            {
                return;
            }

            bool odometerAvailable =
                allowEditing &&
                IsModernTrailerObject(trailerId) &&
                ModernUnitContainsField(
                    "trailer",
                    trailerId,
                    "odometer") &&
                ModernUnitContainsField(
                    "trailer",
                    trailerId,
                    "odometer_float_part");

            bool selectedIsActive =
                !String.IsNullOrWhiteSpace(trailerId) &&
                trailerId.Equals(
                    ModernActiveTrailerId,
                    StringComparison.OrdinalIgnoreCase);

            bool cargoMassAvailable =
                allowEditing &&
                selectedIsActive &&
                ModernUnitContainsField(
                    "trailer",
                    trailerId,
                    "cargo_mass");

            ModernAdvancedValuesLoading = true;

            try
            {
                ModernTrailerOdometerControl.Enabled =
                    odometerAvailable;
                ModernTrailerCargoMassControl.Enabled =
                    cargoMassAvailable;

                if (odometerAvailable || cargoMassAvailable)
                {
                    Trailer trailerData =
                        (Trailer)SiiNunitData.SiiNitems[trailerId];

                    if (odometerAvailable)
                    {
                        ModernTrailerOdometerControl.Value =
                            ClampModernNumericValue(
                                ModernTrailerOdometerControl,
                                Decimal.Round(
                                    ModernCombinedOdometer(
                                        trailerData.odometer,
                                        trailerData.odometer_float_part),
                                    ModernTrailerOdometerControl.DecimalPlaces));
                    }

                    if (cargoMassAvailable)
                    {
                        float cargoMass =
                            (float)trailerData.cargo_mass;

                        decimal safeCargoMass =
                            Single.IsNaN(cargoMass) ||
                            Single.IsInfinity(cargoMass)
                                ? 0M
                                : (decimal)cargoMass;

                        ModernTrailerCargoMassControl.Value =
                            ClampModernNumericValue(
                                ModernTrailerCargoMassControl,
                                Decimal.Round(
                                    safeCargoMass,
                                    ModernTrailerCargoMassControl.DecimalPlaces));
                    }
                }
            }
            finally
            {
                ModernAdvancedValuesLoading = false;
            }
        }

        private void UpdateModernTruckFleetEditUiH6()
        {
            if (!IsModernCompatibilityMode())
                return;

            string selectedTruckId =
                GetSelectedModernTruckIdH6();

            bool allowEditing =
                CanEditModernTruckDataH6(selectedTruckId);

            bool detailsAvailable = allowEditing;

            if (allowEditing)
            {
                try
                {
                    UpdateTruckPanelDetails();
                }
                catch (Exception ex)
                {
                    detailsAvailable = false;
                    IO_Utilities.ErrorLogWriter(
                        "RC5.1H6 isolated a fleet Truck detail UI error: " +
                        ex.Message +
                        Environment.NewLine +
                        ex.StackTrace);
                }
            }

            SetModernTruckEditingEnabled(
                allowEditing && detailsAvailable);

            RefreshModernSelectedTruckAdvancedH6(
                selectedTruckId,
                allowEditing && detailsAvailable);

            if (allowEditing && detailsAvailable)
            {
                toolTipMain.SetToolTip(
                    comboBoxUserTruckCompanyTrucks,
                    "H6: manutenção segura liberada para este caminhão próprio. " +
                    "Reparo, combustível, placa e odômetro serão gravados cirurgicamente.");
            }
        }

        private void UpdateModernTrailerFleetEditUiH6()
        {
            if (!IsModernCompatibilityMode())
                return;

            string selectedTrailerId =
                GetSelectedModernTrailerIdH6();

            bool allowEditing =
                CanEditModernTrailerDataH6(selectedTrailerId);

            RefreshModernSelectedTrailerAdvancedH6(
                selectedTrailerId,
                allowEditing);

            if (allowEditing)
            {
                bool selectedIsActive =
                    selectedTrailerId.Equals(
                        ModernActiveTrailerId,
                        StringComparison.OrdinalIgnoreCase);

                toolTipMain.SetToolTip(
                    comboBoxUserTrailerCompanyTrailers,
                    selectedIsActive
                        ? "H6: reboque ativo editável; massa da carga também disponível."
                        : "H6: reboque próprio editável para reparo, placa e odômetro. " +
                          "Massa da carga permanece exclusiva do reboque ativo.");
            }
        }

        private void InitializeModernFleetEditingH6()
        {
            if (!ModernFleetBaselineIsCurrentH6())
                CaptureModernFleetBaselinesH6();

            UpdateModernTruckFleetEditUiH6();
            UpdateModernTrailerFleetEditUiH6();

            IO_Utilities.LogWriter(
                "RC5.1H6 owned fleet maintenance UI initialized.");
        }
    }
}
