using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using TS_SE_Tool.Save.Items;
using TS_SE_Tool.Utilities;

namespace TS_SE_Tool
{
    public partial class FormMain
    {
        // RC5 uses the ETS2 1.60 player.assigned_vehicles -> player_vehicles graph.
        // The design was cross-checked against Save Edit Tool 0.9.4 (MIT, xLieferant).
        private static readonly Regex ModernUnitHeaderRegex =
            new Regex(@"^([A-Za-z0-9_]+)\s*:\s*([^\s{]+)\s*\{",
                RegexOptions.Compiled);

        private string ModernActiveTruckId = "";
        private string ModernActiveTrailerId = "";
        private string ModernActivePlayerVehicleSlotId = "";

        private string ModernPendingTruckSwitchPreviousTruckId = "";
        private string ModernPendingTruckSwitchTargetTruckId = "";
        private string ModernPendingTruckSwitchSourceSlotId = "";
        private string ModernPendingTruckSwitchTargetSlotId = "";
        private string ModernPendingTruckSwitchPreviousInventorySlotId = "";
        private bool ModernTruckSwitchEventsAttached = false;
        private bool ModernActiveTruckEditingAvailable = true;
        private bool ModernTrailerBrowseEventsAttachedH2 = false;
        private bool ModernTrailerUiRefreshingH2 = false;

        private readonly HashSet<string> ModernTrailerChainIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string ModernBaselineSavePath = "";
        private DateTime ModernBaselineTimestamp = DateTime.MinValue;
        private ModernVehicleSnapshot ModernTruckBaseline = null;
        private readonly Dictionary<string, ModernVehicleSnapshot> ModernTrailerBaselines =
            new Dictionary<string, ModernVehicleSnapshot>(StringComparer.OrdinalIgnoreCase);

        private TableLayoutPanel ModernTruckAdvancedPanel = null;
        private TableLayoutPanel ModernTrailerAdvancedPanel = null;
        private NumericUpDown ModernTruckOdometerControl = null;
        private NumericUpDown ModernTrailerOdometerControl = null;
        private NumericUpDown ModernTrailerCargoMassControl = null;
        private bool ModernAdvancedValuesLoading = false;
        private string ModernOriginalTruckShareText = null;
        private string ModernOriginalTrailerShareText = null;

        private sealed class ModernVehicleSnapshot
        {
            internal Dictionary<string, string> Scalars =
                new Dictionary<string, string>(StringComparer.Ordinal);

            internal Dictionary<string, List<string>> Arrays =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }

        private sealed class ModernPlayerVehicleSlotInfo
        {
            internal string SlotId = "";
            internal string VehicleId = "";
            internal string TrailerId = "";
        }

        private sealed class ModernTruckSwitchPlan
        {
            internal string PlayerId = "";
            internal string PreviousTruckId = "";
            internal string TargetTruckId = "";
            internal string SourceSlotId = "";
            internal string TargetSlotId = "";
            internal string PreviousInventorySlotId = "";
            internal string SourceTrailerId = "";
            internal string TargetTrailerId = "";
        }

        private bool TryParseModernUnitHeader(
            string line,
            out string unitType,
            out string unitId)
        {
            unitType = "";
            unitId = "";

            Match match = ModernUnitHeaderRegex.Match((line ?? "").Trim());
            if (!match.Success)
                return false;

            unitType = match.Groups[1].Value;
            unitId = match.Groups[2].Value;
            return !String.IsNullOrWhiteSpace(unitType) &&
                   !String.IsNullOrWhiteSpace(unitId);
        }

        private string NormalizeModernReference(string value)
        {
            string normalized = (value ?? "").Trim().Trim('"');

            if (String.IsNullOrWhiteSpace(normalized) ||
                normalized.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("nil", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return normalized;
        }

        private bool TryGetFirstModernUnitId(
            string[] lines,
            string requestedUnitType,
            out string unitId)
        {
            unitId = "";

            if (lines == null)
                return false;

            foreach (string line in lines)
            {
                string unitType;
                string candidateId;

                if (TryParseModernUnitHeader(line, out unitType, out candidateId) &&
                    unitType.Equals(requestedUnitType, StringComparison.Ordinal))
                {
                    unitId = candidateId;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetModernUnitField(
            string[] lines,
            string requestedUnitType,
            string requestedUnitId,
            string requestedField,
            out string value)
        {
            value = "";

            if (lines == null ||
                String.IsNullOrWhiteSpace(requestedUnitType) ||
                String.IsNullOrWhiteSpace(requestedUnitId))
            {
                return false;
            }

            int start = -1;
            int depth = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string unitType;
                string unitId;

                if (start < 0)
                {
                    if (!TryParseModernUnitHeader(lines[i], out unitType, out unitId))
                        continue;

                    if (!unitType.Equals(requestedUnitType, StringComparison.Ordinal) ||
                        !unitId.Equals(requestedUnitId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    start = i;
                    depth = lines[i].Count(x => x == '{') -
                            lines[i].Count(x => x == '}');
                    continue;
                }

                depth += lines[i].Count(x => x == '{');
                depth -= lines[i].Count(x => x == '}');

                string trimmed = (lines[i] ?? "").Trim();
                int colonPosition = trimmed.IndexOf(':');

                if (colonPosition > 0)
                {
                    string tag = trimmed.Substring(0, colonPosition).Trim();
                    if (tag.Equals(requestedField, StringComparison.Ordinal))
                    {
                        value = trimmed.Substring(colonPosition + 1).Trim();
                        return true;
                    }
                }

                if (depth <= 0)
                    return false;
            }

            return false;
        }

        private bool IsModernTruckObject(string id)
        {
            if (String.IsNullOrWhiteSpace(id) ||
                SiiNunitData == null ||
                SiiNunitData.SiiNitems == null ||
                !SiiNunitData.SiiNitems.ContainsKey(id))
            {
                return false;
            }

            object value = (object)SiiNunitData.SiiNitems[id];
            return value is Vehicle;
        }

        private bool IsModernTrailerObject(string id)
        {
            if (String.IsNullOrWhiteSpace(id) ||
                SiiNunitData == null ||
                SiiNunitData.SiiNitems == null ||
                !SiiNunitData.SiiNitems.ContainsKey(id))
            {
                return false;
            }

            object value = (object)SiiNunitData.SiiNitems[id];
            return value is Trailer;
        }

        private bool ResolveModernActiveVehicleReferences()
        {
            ModernActiveTruckId = "";
            ModernActiveTrailerId = "";
            ModernActivePlayerVehicleSlotId = "";
            ModernTrailerChainIds.Clear();

            if (!IsModernCompatibilityMode() ||
                tempSavefileInMemory == null ||
                tempSavefileInMemory.Length == 0)
            {
                return false;
            }

            string playerId;
            if (!TryGetFirstModernUnitId(
                    tempSavefileInMemory,
                    "player",
                    out playerId))
            {
                IO_Utilities.LogWriter(
                    "RC5 could not locate the player block in the decoded save.");
                return false;
            }

            string assignedVehiclesRaw;
            string assignedVehiclesId = "";

            if (TryGetModernUnitField(
                    tempSavefileInMemory,
                    "player",
                    playerId,
                    "assigned_vehicles",
                    out assignedVehiclesRaw))
            {
                assignedVehiclesId =
                    NormalizeModernReference(assignedVehiclesRaw);
            }

            if (!String.IsNullOrWhiteSpace(assignedVehiclesId))
            {
                ModernActivePlayerVehicleSlotId = assignedVehiclesId;

                string vehicleRaw;
                string trailerRaw;

                if (TryGetModernUnitField(
                        tempSavefileInMemory,
                        "player_vehicles",
                        assignedVehiclesId,
                        "vehicle",
                        out vehicleRaw))
                {
                    ModernActiveTruckId =
                        NormalizeModernReference(vehicleRaw);
                }

                if (TryGetModernUnitField(
                        tempSavefileInMemory,
                        "player_vehicles",
                        assignedVehiclesId,
                        "trailer",
                        out trailerRaw))
                {
                    ModernActiveTrailerId =
                        NormalizeModernReference(trailerRaw);
                }
            }

            // Compatibility fallback for saves that still use direct pointers.
            if (String.IsNullOrWhiteSpace(ModernActiveTruckId))
            {
                string directTruck;

                if (TryGetModernUnitField(
                        tempSavefileInMemory,
                        "player",
                        playerId,
                        "assigned_truck",
                        out directTruck))
                {
                    ModernActiveTruckId =
                        NormalizeModernReference(directTruck);
                }

                if (String.IsNullOrWhiteSpace(ModernActiveTruckId) &&
                    TryGetModernUnitField(
                        tempSavefileInMemory,
                        "player",
                        playerId,
                        "my_truck",
                        out directTruck))
                {
                    ModernActiveTruckId =
                        NormalizeModernReference(directTruck);
                }
            }

            if (String.IsNullOrWhiteSpace(ModernActiveTrailerId))
            {
                string directTrailer;

                if (TryGetModernUnitField(
                        tempSavefileInMemory,
                        "player",
                        playerId,
                        "assigned_trailer",
                        out directTrailer))
                {
                    ModernActiveTrailerId =
                        NormalizeModernReference(directTrailer);
                }

                if (String.IsNullOrWhiteSpace(ModernActiveTrailerId) &&
                    TryGetModernUnitField(
                        tempSavefileInMemory,
                        "player",
                        playerId,
                        "my_trailer",
                        out directTrailer))
                {
                    ModernActiveTrailerId =
                        NormalizeModernReference(directTrailer);
                }
            }

            if (!IsModernTruckObject(ModernActiveTruckId))
                ModernActiveTruckId = "";

            if (!IsModernTrailerObject(ModernActiveTrailerId))
                ModernActiveTrailerId = "";

            if (!String.IsNullOrWhiteSpace(ModernActiveTrailerId))
            {
                string nextTrailer = ModernActiveTrailerId;
                int guard = 0;

                while (!String.IsNullOrWhiteSpace(nextTrailer) &&
                       guard < 16 &&
                       ModernTrailerChainIds.Add(nextTrailer))
                {
                    guard++;

                    if (!IsModernTrailerObject(nextTrailer))
                        break;

                    Trailer trailerData =
                        (Trailer)SiiNunitData.SiiNitems[nextTrailer];

                    nextTrailer =
                        NormalizeModernReference(trailerData.slave_trailer);
                }
            }

            // Existing tab code expects these legacy in-memory pointers.
            // RC5 never serializes these legacy player fields for modern saves.
            if (SiiNunitData.Player != null)
            {
                SiiNunitData.Player.assigned_truck =
                    String.IsNullOrWhiteSpace(ModernActiveTruckId)
                        ? "null"
                        : ModernActiveTruckId;

                SiiNunitData.Player.assigned_trailer =
                    String.IsNullOrWhiteSpace(ModernActiveTrailerId)
                        ? "null"
                        : ModernActiveTrailerId;
            }

            IO_Utilities.LogWriter(
                "RC5 active vehicle resolution: truck=" +
                (String.IsNullOrWhiteSpace(ModernActiveTruckId)
                    ? "null"
                    : ModernActiveTruckId) +
                ", trailer=" +
                (String.IsNullOrWhiteSpace(ModernActiveTrailerId)
                    ? "null"
                    : ModernActiveTrailerId));

            return !String.IsNullOrWhiteSpace(ModernActiveTruckId);
        }

        private ModernVehicleSnapshot CaptureModernTruckSnapshot(
            Vehicle data)
        {
            ModernVehicleSnapshot snapshot = new ModernVehicleSnapshot();

            snapshot.Scalars["engine_wear"] = data.engine_wear.ToString();
            snapshot.Scalars["transmission_wear"] = data.transmission_wear.ToString();
            snapshot.Scalars["cabin_wear"] = data.cabin_wear.ToString();
            snapshot.Scalars["chassis_wear"] = data.chassis_wear.ToString();
            snapshot.Scalars["engine_wear_unfixable"] = data.engine_wear_unfixable.ToString();
            snapshot.Scalars["transmission_wear_unfixable"] = data.transmission_wear_unfixable.ToString();
            snapshot.Scalars["cabin_wear_unfixable"] = data.cabin_wear_unfixable.ToString();
            snapshot.Scalars["chassis_wear_unfixable"] = data.chassis_wear_unfixable.ToString();
            snapshot.Scalars["fuel_relative"] = data.fuel_relative.ToString();
            snapshot.Scalars["license_plate"] = data.license_plate.ToString();
            snapshot.Scalars["odometer"] =
                data.odometer.ToString(CultureInfo.InvariantCulture);
            snapshot.Scalars["odometer_float_part"] =
                data.odometer_float_part.ToString();
            snapshot.Scalars["integrity_odometer"] =
                data.integrity_odometer.ToString(CultureInfo.InvariantCulture);
            snapshot.Scalars["integrity_odometer_float_part"] =
                data.integrity_odometer_float_part.ToString();

            snapshot.Arrays["wheels_wear"] =
                data.wheels_wear.Select(x => x.ToString()).ToList();
            snapshot.Arrays["wheels_wear_unfixable"] =
                data.wheels_wear_unfixable.Select(x => x.ToString()).ToList();

            return snapshot;
        }

        private ModernVehicleSnapshot CaptureModernTrailerSnapshot(
            Trailer data)
        {
            ModernVehicleSnapshot snapshot = new ModernVehicleSnapshot();

            snapshot.Scalars["cargo_mass"] = data.cargo_mass.ToString();
            snapshot.Scalars["cargo_damage"] = data.cargo_damage.ToString();
            snapshot.Scalars["trailer_body_wear"] = data.trailer_body_wear.ToString();
            snapshot.Scalars["chassis_wear"] = data.chassis_wear.ToString();
            snapshot.Scalars["trailer_body_wear_unfixable"] =
                data.trailer_body_wear_unfixable.ToString();
            snapshot.Scalars["chassis_wear_unfixable"] =
                data.chassis_wear_unfixable.ToString();
            snapshot.Scalars["license_plate"] = data.license_plate.ToString();
            snapshot.Scalars["odometer"] =
                data.odometer.ToString(CultureInfo.InvariantCulture);
            snapshot.Scalars["odometer_float_part"] =
                data.odometer_float_part.ToString();
            snapshot.Scalars["integrity_odometer"] =
                data.integrity_odometer.ToString(CultureInfo.InvariantCulture);
            snapshot.Scalars["integrity_odometer_float_part"] =
                data.integrity_odometer_float_part.ToString();

            snapshot.Arrays["wheels_wear"] =
                data.wheels_wear.Select(x => x.ToString()).ToList();
            snapshot.Arrays["wheels_wear_unfixable"] =
                data.wheels_wear_unfixable.Select(x => x.ToString()).ToList();

            return snapshot;
        }

        private void CaptureModernVehicleBaselines()
        {
            ModernPendingTruckSwitchPreviousTruckId = "";
            ModernPendingTruckSwitchTargetTruckId = "";
            ModernPendingTruckSwitchSourceSlotId = "";
            ModernPendingTruckSwitchTargetSlotId = "";
            ModernPendingTruckSwitchPreviousInventorySlotId = "";

            ModernTruckBaseline = null;
            ModernTrailerBaselines.Clear();

            if (!ResolveModernActiveVehicleReferences())
            {
                ModernBaselineSavePath = Globals.SelectedSavePath ?? "";
                ModernBaselineTimestamp = LastModifiedTimestamp;
                return;
            }

            if (!String.IsNullOrWhiteSpace(ModernActiveTruckId) &&
                IsModernTruckObject(ModernActiveTruckId))
            {
                ModernTruckBaseline = CaptureModernTruckSnapshot(
                    (Vehicle)SiiNunitData.SiiNitems[ModernActiveTruckId]);
            }

            foreach (string trailerId in ModernTrailerChainIds)
            {
                if (!IsModernTrailerObject(trailerId))
                    continue;

                ModernTrailerBaselines[trailerId] =
                    CaptureModernTrailerSnapshot(
                        (Trailer)SiiNunitData.SiiNitems[trailerId]);
            }

            ModernBaselineSavePath = Globals.SelectedSavePath ?? "";
            ModernBaselineTimestamp = LastModifiedTimestamp;
        }

        private bool ModernVehicleBaselineIsCurrent()
        {
            return String.Equals(
                       ModernBaselineSavePath,
                       Globals.SelectedSavePath ?? "",
                       StringComparison.OrdinalIgnoreCase) &&
                   ModernBaselineTimestamp == LastModifiedTimestamp;
        }

        private Dictionary<string, string> GetChangedModernScalars(
            ModernVehicleSnapshot baseline,
            ModernVehicleSnapshot current)
        {
            Dictionary<string, string> changed =
                new Dictionary<string, string>(StringComparer.Ordinal);

            if (baseline == null || current == null)
                return changed;

            foreach (KeyValuePair<string, string> item in current.Scalars)
            {
                string oldValue;
                if (!baseline.Scalars.TryGetValue(item.Key, out oldValue) ||
                    !String.Equals(oldValue, item.Value, StringComparison.Ordinal))
                {
                    changed[item.Key] = item.Value;
                }
            }

            return changed;
        }

        private Dictionary<string, List<string>> GetChangedModernArrays(
            ModernVehicleSnapshot baseline,
            ModernVehicleSnapshot current)
        {
            Dictionary<string, List<string>> changed =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);

            if (baseline == null || current == null)
                return changed;

            foreach (KeyValuePair<string, List<string>> item in current.Arrays)
            {
                List<string> oldValue;

                if (!baseline.Arrays.TryGetValue(item.Key, out oldValue) ||
                    !oldValue.SequenceEqual(item.Value, StringComparer.Ordinal))
                {
                    changed[item.Key] = new List<string>(item.Value);
                }
            }

            return changed;
        }

        private string PatchModernUnitFields(
            string content,
            string unitType,
            string unitId,
            Dictionary<string, string> scalarChanges,
            Dictionary<string, List<string>> arrayChanges)
        {
            if ((scalarChanges == null || scalarChanges.Count == 0) &&
                (arrayChanges == null || arrayChanges.Count == 0))
            {
                return content;
            }

            string[] lines = content
                .Replace("\r\n", "\n")
                .Split(new char[] { '\n' });

            int start = -1;
            int end = -1;
            int depth = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string foundType;
                string foundId;

                if (start < 0)
                {
                    if (!TryParseModernUnitHeader(lines[i], out foundType, out foundId))
                        continue;

                    if (!foundType.Equals(unitType, StringComparison.Ordinal) ||
                        !foundId.Equals(unitId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    start = i;
                    depth = lines[i].Count(x => x == '{') -
                            lines[i].Count(x => x == '}');

                    if (depth <= 0)
                        end = i;

                    continue;
                }

                depth += lines[i].Count(x => x == '{');
                depth -= lines[i].Count(x => x == '}');

                if (depth <= 0)
                {
                    end = i;
                    break;
                }
            }

            if (start < 0 || end < start)
            {
                throw new InvalidDataException(
                    "RC5 could not locate " + unitType + " block " + unitId + ".");
            }

            HashSet<string> replacedScalars =
                new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, int> declaredArrayCounts =
                new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> replacedArrayEntries =
                new Dictionary<string, int>(StringComparer.Ordinal);

            if (arrayChanges != null)
            {
                foreach (string arrayName in arrayChanges.Keys)
                {
                    declaredArrayCounts[arrayName] = -1;
                    replacedArrayEntries[arrayName] = 0;
                }
            }

            for (int i = start + 1; i < end; i++)
            {
                string line = lines[i] ?? "";
                string trimmed = line.Trim();
                int colonPosition = trimmed.IndexOf(':');

                if (colonPosition <= 0)
                    continue;

                string tag = trimmed.Substring(0, colonPosition).Trim();
                string indentation =
                    line.Substring(0, line.Length - line.TrimStart().Length);

                string scalarValue;
                if (scalarChanges != null &&
                    scalarChanges.TryGetValue(tag, out scalarValue))
                {
                    lines[i] = indentation + tag + ": " + scalarValue;
                    replacedScalars.Add(tag);
                    continue;
                }

                if (arrayChanges == null)
                    continue;

                foreach (KeyValuePair<string, List<string>> arrayChange
                         in arrayChanges)
                {
                    string arrayName = arrayChange.Key;

                    if (tag.Equals(arrayName, StringComparison.Ordinal))
                    {
                        int declaredCount;
                        string rawCount =
                            trimmed.Substring(colonPosition + 1).Trim();

                        if (!Int32.TryParse(
                                rawCount,
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out declaredCount) ||
                            declaredCount < 0)
                        {
                            throw new InvalidDataException(
                                "RC5 found an invalid " + arrayName +
                                " count in " + unitId + ".");
                        }

                        if (arrayChange.Value.Count != 0 &&
                            arrayChange.Value.Count != declaredCount)
                        {
                            throw new InvalidDataException(
                                "RC5 refused to rewrite " + arrayName +
                                " because the parsed item count does not match the save.");
                        }

                        declaredArrayCounts[arrayName] = declaredCount;
                        break;
                    }

                    Match indexMatch = Regex.Match(
                        tag,
                        "^" + Regex.Escape(arrayName) + @"\[(\d+)\]$");

                    if (!indexMatch.Success)
                        continue;

                    int index = Int32.Parse(
                        indexMatch.Groups[1].Value,
                        CultureInfo.InvariantCulture);

                    if (arrayChange.Value.Count != 0 &&
                        index >= arrayChange.Value.Count)
                    {
                        throw new InvalidDataException(
                            "RC5 found an invalid " + arrayName +
                            " index in " + unitId + ".");
                    }

                    string replacement =
                        arrayChange.Value.Count == 0
                            ? "0"
                            : arrayChange.Value[index];

                    lines[i] = indentation + tag + ": " + replacement;
                    replacedArrayEntries[arrayName] =
                        replacedArrayEntries[arrayName] + 1;
                    break;
                }
            }

            if (scalarChanges != null)
            {
                string[] missingScalars = scalarChanges.Keys
                    .Where(x => !replacedScalars.Contains(x))
                    .ToArray();

                if (missingScalars.Length > 0)
                {
                    throw new InvalidDataException(
                        "RC5 could not locate edited fields in " +
                        unitType + " " + unitId + ": " +
                        String.Join(", ", missingScalars));
                }
            }

            if (arrayChanges != null)
            {
                foreach (string arrayName in arrayChanges.Keys)
                {
                    int declared = declaredArrayCounts[arrayName];
                    int replaced = replacedArrayEntries[arrayName];

                    if (declared < 0 || replaced != declared)
                    {
                        throw new InvalidDataException(
                            "RC5 refused to rewrite " + arrayName +
                            " in " + unitId +
                            " because its indexed entries are incomplete.");
                    }
                }
            }

            return String.Join(Environment.NewLine, lines);
        }

        private string PatchModernVehicleData(string content)
        {
            ResolveModernActiveVehicleReferences();

            bool hasEditableVehicle =
                !String.IsNullOrWhiteSpace(ModernActiveTruckId) ||
                ModernTrailerChainIds.Count > 0;

            if (!hasEditableVehicle)
                return content;

            if (!ModernVehicleBaselineIsCurrent())
            {
                throw new InvalidOperationException(
                    "RC5 vehicle baseline is unavailable or stale. " +
                    "Reload the save before writing.");
            }

            string output = content;

            if (!String.IsNullOrWhiteSpace(ModernActiveTruckId) &&
                ModernTruckBaseline != null &&
                IsModernTruckObject(ModernActiveTruckId))
            {
                Vehicle currentTruck =
                    (Vehicle)SiiNunitData.SiiNitems[ModernActiveTruckId];
                ModernVehicleSnapshot currentSnapshot =
                    CaptureModernTruckSnapshot(currentTruck);

                output = PatchModernUnitFields(
                    output,
                    "vehicle",
                    ModernActiveTruckId,
                    GetChangedModernScalars(ModernTruckBaseline, currentSnapshot),
                    GetChangedModernArrays(ModernTruckBaseline, currentSnapshot));
            }

            foreach (string trailerId in ModernTrailerChainIds)
            {
                ModernVehicleSnapshot baseline;

                if (!ModernTrailerBaselines.TryGetValue(trailerId, out baseline) ||
                    !IsModernTrailerObject(trailerId))
                {
                    continue;
                }

                Trailer currentTrailer =
                    (Trailer)SiiNunitData.SiiNitems[trailerId];
                ModernVehicleSnapshot currentSnapshot =
                    CaptureModernTrailerSnapshot(currentTrailer);

                output = PatchModernUnitFields(
                    output,
                    "trailer",
                    trailerId,
                    GetChangedModernScalars(baseline, currentSnapshot),
                    GetChangedModernArrays(baseline, currentSnapshot));
            }

            return PatchModernFleetVehicleDataH6(output);
        }

        private NumericUpDown CreateModernAdvancedNumber(
            string name,
            int decimalPlaces,
            decimal maximum,
            decimal increment)
        {
            NumericUpDown control = new NumericUpDown();
            control.Name = name;
            control.DecimalPlaces = decimalPlaces;
            control.Minimum = 0M;
            control.Maximum = maximum;
            control.Increment = increment;
            control.ThousandsSeparator = true;
            control.Dock = DockStyle.Fill;
            control.TextAlign = HorizontalAlignment.Right;
            control.Margin = new Padding(3);
            return control;
        }

        private Label CreateModernAdvancedLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Dock = DockStyle.Fill;
            label.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            label.Margin = new Padding(3);
            return label;
        }

        private void EnsureModernAdvancedVehicleControls()
        {
            if (ModernTruckAdvancedPanel == null)
            {
                ModernOriginalTruckShareText =
                    groupBoxUserTruckShareTruckSettings.Text;

                ModernTruckAdvancedPanel = new TableLayoutPanel();
                ModernTruckAdvancedPanel.Name =
                    "tableLayoutPanelModernTruckAdvanced";
                ModernTruckAdvancedPanel.ColumnCount = 2;
                ModernTruckAdvancedPanel.ColumnStyles.Add(
                    new ColumnStyle(SizeType.Percent, 45F));
                ModernTruckAdvancedPanel.ColumnStyles.Add(
                    new ColumnStyle(SizeType.Percent, 55F));
                ModernTruckAdvancedPanel.RowCount = 2;
                ModernTruckAdvancedPanel.RowStyles.Add(
                    new RowStyle(SizeType.Percent, 55F));
                ModernTruckAdvancedPanel.RowStyles.Add(
                    new RowStyle(SizeType.Percent, 45F));
                ModernTruckAdvancedPanel.Dock = DockStyle.Fill;
                ModernTruckAdvancedPanel.Visible = false;

                ModernTruckOdometerControl =
                    CreateModernAdvancedNumber(
                        "numericModernTruckOdometer",
                        3,
                        4294967295M,
                        1M);

                ModernTruckOdometerControl.ValueChanged +=
                    ModernTruckOdometer_ValueChanged;

                ModernTruckAdvancedPanel.Controls.Add(
                    CreateModernAdvancedLabel("Odometer (km)"),
                    0,
                    0);

                ModernTruckAdvancedPanel.Controls.Add(
                    ModernTruckOdometerControl,
                    1,
                    0);

                Label integrityInfo = CreateModernAdvancedLabel(
                    "Integrity odometer is preserved.");
                integrityInfo.AutoEllipsis = true;
                ModernTruckAdvancedPanel.Controls.Add(
                    integrityInfo,
                    0,
                    1);
                ModernTruckAdvancedPanel.SetColumnSpan(
                    integrityInfo,
                    2);

                groupBoxUserTruckShareTruckSettings.Controls.Add(
                    ModernTruckAdvancedPanel);
                ModernTruckAdvancedPanel.BringToFront();
            }

            if (ModernTrailerAdvancedPanel == null)
            {
                ModernOriginalTrailerShareText =
                    groupBoxUserTrailerShareTrailerSettings.Text;

                ModernTrailerAdvancedPanel = new TableLayoutPanel();
                ModernTrailerAdvancedPanel.Name =
                    "tableLayoutPanelModernTrailerAdvanced";
                ModernTrailerAdvancedPanel.ColumnCount = 2;
                ModernTrailerAdvancedPanel.ColumnStyles.Add(
                    new ColumnStyle(SizeType.Percent, 45F));
                ModernTrailerAdvancedPanel.ColumnStyles.Add(
                    new ColumnStyle(SizeType.Percent, 55F));
                ModernTrailerAdvancedPanel.RowCount = 3;
                ModernTrailerAdvancedPanel.RowStyles.Add(
                    new RowStyle(SizeType.Percent, 34F));
                ModernTrailerAdvancedPanel.RowStyles.Add(
                    new RowStyle(SizeType.Percent, 34F));
                ModernTrailerAdvancedPanel.RowStyles.Add(
                    new RowStyle(SizeType.Percent, 32F));
                ModernTrailerAdvancedPanel.Dock = DockStyle.Fill;
                ModernTrailerAdvancedPanel.Visible = false;

                ModernTrailerOdometerControl =
                    CreateModernAdvancedNumber(
                        "numericModernTrailerOdometer",
                        3,
                        4294967295M,
                        1M);

                ModernTrailerCargoMassControl =
                    CreateModernAdvancedNumber(
                        "numericModernTrailerCargoMass",
                        1,
                        100000000M,
                        100M);

                ModernTrailerOdometerControl.ValueChanged +=
                    ModernTrailerOdometer_ValueChanged;

                ModernTrailerCargoMassControl.ValueChanged +=
                    ModernTrailerCargoMass_ValueChanged;

                ModernTrailerAdvancedPanel.Controls.Add(
                    CreateModernAdvancedLabel("Odometer (km)"),
                    0,
                    0);

                ModernTrailerAdvancedPanel.Controls.Add(
                    ModernTrailerOdometerControl,
                    1,
                    0);

                ModernTrailerAdvancedPanel.Controls.Add(
                    CreateModernAdvancedLabel("Cargo mass (kg)"),
                    0,
                    1);

                ModernTrailerAdvancedPanel.Controls.Add(
                    ModernTrailerCargoMassControl,
                    1,
                    1);

                Label trailerInfo = CreateModernAdvancedLabel(
                    "Only the attached main trailer is edited.");
                trailerInfo.AutoEllipsis = true;
                ModernTrailerAdvancedPanel.Controls.Add(
                    trailerInfo,
                    0,
                    2);
                ModernTrailerAdvancedPanel.SetColumnSpan(
                    trailerInfo,
                    2);

                groupBoxUserTrailerShareTrailerSettings.Controls.Add(
                    ModernTrailerAdvancedPanel);
                ModernTrailerAdvancedPanel.BringToFront();
            }
        }

        private void ConfigureModernAdvancedVehiclePanels(
            bool modernMode)
        {
            if (modernMode)
                EnsureModernAdvancedVehicleControls();

            if (ModernTruckAdvancedPanel == null ||
                ModernTrailerAdvancedPanel == null)
            {
                return;
            }

            ModernTruckAdvancedPanel.Visible = modernMode;
            ModernTrailerAdvancedPanel.Visible = modernMode;
            tableLayoutPanel7.Visible = !modernMode;
            tableLayoutPanel14.Visible = !modernMode;

            if (modernMode)
            {
                groupBoxUserTruckShareTruckSettings.Text =
                    "Advanced Truck Data (RC5)";
                groupBoxUserTrailerShareTrailerSettings.Text =
                    "Advanced Trailer Data (RC5)";
                groupBoxUserTruckShareTruckSettings.Enabled = true;
                groupBoxUserTrailerShareTrailerSettings.Enabled = true;
                ModernTruckAdvancedPanel.BringToFront();
                ModernTrailerAdvancedPanel.BringToFront();
            }
            else
            {
                if (ModernOriginalTruckShareText != null)
                {
                    groupBoxUserTruckShareTruckSettings.Text =
                        ModernOriginalTruckShareText;
                }

                if (ModernOriginalTrailerShareText != null)
                {
                    groupBoxUserTrailerShareTrailerSettings.Text =
                        ModernOriginalTrailerShareText;
                }
            }
        }

        private bool ModernUnitContainsField(
            string unitType,
            string unitId,
            string fieldName)
        {
            string rawValue;
            return TryGetModernUnitField(
                tempSavefileInMemory,
                unitType,
                unitId,
                fieldName,
                out rawValue);
        }

        private decimal ClampModernNumericValue(
            NumericUpDown control,
            decimal value)
        {
            if (value < control.Minimum)
                return control.Minimum;

            if (value > control.Maximum)
                return control.Maximum;

            return value;
        }

        private decimal ModernCombinedOdometer(
            uint integerPart,
            Save.DataFormat.SCS_Float floatPart)
        {
            float fraction = (float)floatPart;

            if (Single.IsNaN(fraction) ||
                Single.IsInfinity(fraction))
            {
                fraction = 0F;
            }

            decimal result = integerPart + (decimal)fraction;
            if (result < 0M)
                return 0M;

            return result;
        }

        private void PopulateModernAdvancedVehicleControls()
        {
            if (ModernTruckAdvancedPanel == null ||
                ModernTrailerAdvancedPanel == null)
            {
                return;
            }

            ModernAdvancedValuesLoading = true;

            try
            {
                bool truckOdometerAvailable =
                    !String.IsNullOrWhiteSpace(ModernActiveTruckId) &&
                    IsModernTruckObject(ModernActiveTruckId) &&
                    ModernUnitContainsField(
                        "vehicle",
                        ModernActiveTruckId,
                        "odometer") &&
                    ModernUnitContainsField(
                        "vehicle",
                        ModernActiveTruckId,
                        "odometer_float_part");

                ModernTruckOdometerControl.Enabled =
                    truckOdometerAvailable;

                if (truckOdometerAvailable)
                {
                    Vehicle truckData =
                        (Vehicle)SiiNunitData.SiiNitems[
                            ModernActiveTruckId];

                    decimal truckOdometer =
                        ModernCombinedOdometer(
                            truckData.odometer,
                            truckData.odometer_float_part);

                    ModernTruckOdometerControl.Value =
                        ClampModernNumericValue(
                            ModernTruckOdometerControl,
                            Decimal.Round(
                                truckOdometer,
                                ModernTruckOdometerControl.DecimalPlaces));
                }

                bool trailerOdometerAvailable =
                    !String.IsNullOrWhiteSpace(ModernActiveTrailerId) &&
                    IsModernTrailerObject(ModernActiveTrailerId) &&
                    ModernUnitContainsField(
                        "trailer",
                        ModernActiveTrailerId,
                        "odometer") &&
                    ModernUnitContainsField(
                        "trailer",
                        ModernActiveTrailerId,
                        "odometer_float_part");

                bool cargoMassAvailable =
                    !String.IsNullOrWhiteSpace(ModernActiveTrailerId) &&
                    IsModernTrailerObject(ModernActiveTrailerId) &&
                    ModernUnitContainsField(
                        "trailer",
                        ModernActiveTrailerId,
                        "cargo_mass");

                ModernTrailerOdometerControl.Enabled =
                    trailerOdometerAvailable;
                ModernTrailerCargoMassControl.Enabled =
                    cargoMassAvailable;

                if (trailerOdometerAvailable ||
                    cargoMassAvailable)
                {
                    Trailer trailerData =
                        (Trailer)SiiNunitData.SiiNitems[
                            ModernActiveTrailerId];

                    if (trailerOdometerAvailable)
                    {
                        decimal trailerOdometer =
                            ModernCombinedOdometer(
                                trailerData.odometer,
                                trailerData.odometer_float_part);

                        ModernTrailerOdometerControl.Value =
                            ClampModernNumericValue(
                                ModernTrailerOdometerControl,
                                Decimal.Round(
                                    trailerOdometer,
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

                toolTipMain.SetToolTip(
                    ModernTruckOdometerControl,
                    "Changes odometer and odometer_float_part. " +
                    "integrity_odometer remains unchanged.");

                toolTipMain.SetToolTip(
                    ModernTrailerOdometerControl,
                    "Changes the attached trailer odometer only.");

                toolTipMain.SetToolTip(
                    ModernTrailerCargoMassControl,
                    "Changes cargo_mass in kilograms on the attached main trailer.");
            }
            finally
            {
                ModernAdvancedValuesLoading = false;
            }
        }

        private void SetModernOdometer(
            decimal input,
            out uint integerPart,
            out float floatPart)
        {
            decimal safeValue = input;

            if (safeValue < 0M)
                safeValue = 0M;

            if (safeValue > 4294967295M)
                safeValue = 4294967295M;

            decimal whole = Decimal.Truncate(safeValue);
            integerPart = Decimal.ToUInt32(whole);
            floatPart = (float)(safeValue - whole);
        }

        private void ModernTruckOdometer_ValueChanged(
            object sender,
            EventArgs e)
        {
            string selectedTruckId =
                GetSelectedModernTruckIdH6();

            if (ModernAdvancedValuesLoading ||
                String.IsNullOrWhiteSpace(selectedTruckId) ||
                !CanEditModernTruckDataH6(selectedTruckId))
            {
                return;
            }

            Vehicle truckData =
                (Vehicle)SiiNunitData.SiiNitems[
                    selectedTruckId];

            uint integerPart;
            float floatPart;

            SetModernOdometer(
                ModernTruckOdometerControl.Value,
                out integerPart,
                out floatPart);

            truckData.odometer = integerPart;
            truckData.odometer_float_part = floatPart;
        }

        private void ModernTrailerOdometer_ValueChanged(
            object sender,
            EventArgs e)
        {
            string selectedTrailerId =
                GetSelectedModernTrailerIdH6();

            if (ModernAdvancedValuesLoading ||
                String.IsNullOrWhiteSpace(selectedTrailerId) ||
                !CanEditModernTrailerDataH6(selectedTrailerId))
            {
                return;
            }

            Trailer trailerData =
                (Trailer)SiiNunitData.SiiNitems[
                    selectedTrailerId];

            uint integerPart;
            float floatPart;

            SetModernOdometer(
                ModernTrailerOdometerControl.Value,
                out integerPart,
                out floatPart);

            trailerData.odometer = integerPart;
            trailerData.odometer_float_part = floatPart;
        }

        private void ModernTrailerCargoMass_ValueChanged(
            object sender,
            EventArgs e)
        {
            if (ModernAdvancedValuesLoading ||
                String.IsNullOrWhiteSpace(ModernActiveTrailerId) ||
                !IsModernTrailerObject(ModernActiveTrailerId))
            {
                return;
            }

            Trailer trailerData =
                (Trailer)SiiNunitData.SiiNitems[
                    ModernActiveTrailerId];

            trailerData.cargo_mass =
                (float)ModernTrailerCargoMassControl.Value;
        }

        private List<string> GetModernUnitIds(
            string[] lines,
            string requestedUnitType)
        {
            List<string> unitIds = new List<string>();

            if (lines == null)
                return unitIds;

            foreach (string line in lines)
            {
                string unitType;
                string unitId;

                if (TryParseModernUnitHeader(
                        line,
                        out unitType,
                        out unitId) &&
                    unitType.Equals(
                        requestedUnitType,
                        StringComparison.Ordinal))
                {
                    unitIds.Add(unitId);
                }
            }

            return unitIds;
        }

        private Dictionary<int, string> GetModernUnitArrayEntries(
            string[] lines,
            string requestedUnitType,
            string requestedUnitId,
            string requestedField)
        {
            Dictionary<int, string> result =
                new Dictionary<int, string>();

            if (lines == null)
                return result;

            int start = -1;
            int depth = 0;
            Regex itemRegex = new Regex(
                "^" +
                Regex.Escape(requestedField) +
                @"\[(\d+)\]$",
                RegexOptions.Compiled);

            for (int i = 0; i < lines.Length; i++)
            {
                string unitType;
                string unitId;

                if (start < 0)
                {
                    if (!TryParseModernUnitHeader(
                            lines[i],
                            out unitType,
                            out unitId))
                    {
                        continue;
                    }

                    if (!unitType.Equals(
                            requestedUnitType,
                            StringComparison.Ordinal) ||
                        !unitId.Equals(
                            requestedUnitId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    start = i;
                    depth = lines[i].Count(x => x == '{') -
                            lines[i].Count(x => x == '}');
                    continue;
                }

                depth += lines[i].Count(x => x == '{');
                depth -= lines[i].Count(x => x == '}');

                string trimmed = (lines[i] ?? "").Trim();
                int colonPosition = trimmed.IndexOf(':');

                if (colonPosition > 0)
                {
                    string tag =
                        trimmed.Substring(0, colonPosition).Trim();

                    Match match = itemRegex.Match(tag);
                    if (match.Success)
                    {
                        int index = Int32.Parse(
                            match.Groups[1].Value,
                            CultureInfo.InvariantCulture);

                        result[index] =
                            trimmed.Substring(colonPosition + 1).Trim();
                    }
                }

                if (depth <= 0)
                    break;
            }

            return result;
        }

        private List<string> GetModernUnitArrayValues(
            string[] lines,
            string requestedUnitType,
            string requestedUnitId,
            string requestedField)
        {
            return GetModernUnitArrayEntries(
                    lines,
                    requestedUnitType,
                    requestedUnitId,
                    requestedField)
                .OrderBy(x => x.Key)
                .Select(x => NormalizeModernReference(x.Value))
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private List<ModernPlayerVehicleSlotInfo>
            GetModernPlayerVehicleSlots(string[] lines)
        {
            List<ModernPlayerVehicleSlotInfo> slots =
                new List<ModernPlayerVehicleSlotInfo>();

            foreach (string slotId in GetModernUnitIds(
                         lines,
                         "player_vehicles"))
            {
                string vehicleRaw;
                string trailerRaw;

                TryGetModernUnitField(
                    lines,
                    "player_vehicles",
                    slotId,
                    "vehicle",
                    out vehicleRaw);

                TryGetModernUnitField(
                    lines,
                    "player_vehicles",
                    slotId,
                    "trailer",
                    out trailerRaw);

                ModernPlayerVehicleSlotInfo slot =
                    new ModernPlayerVehicleSlotInfo();

                slot.SlotId = slotId;
                slot.VehicleId =
                    NormalizeModernReference(vehicleRaw);
                slot.TrailerId =
                    NormalizeModernReference(trailerRaw);

                slots.Add(slot);
            }

            return slots;
        }

        private bool ModernTruckHasValidAccessoryGraph(
            string truckId,
            out string reason)
        {
            reason = "";

            if (!IsModernTruckObject(truckId))
            {
                reason = "O bloco vehicle do caminhão não foi encontrado.";
                return false;
            }

            Vehicle truck =
                (Vehicle)SiiNunitData.SiiNitems[truckId];

            if (truck.accessories == null ||
                truck.accessories.Count == 0)
            {
                reason =
                    "O caminhão não possui uma lista de acessórios válida.";
                return false;
            }

            foreach (string accessoryId in truck.accessories)
            {
                string normalized =
                    NormalizeModernReference(accessoryId);

                if (String.IsNullOrWhiteSpace(normalized))
                    continue;

                if (normalized.StartsWith(
                        "_nameless.",
                        StringComparison.OrdinalIgnoreCase) &&
                    !SiiNunitData.SiiNitems.ContainsKey(normalized))
                {
                    reason =
                        "O caminhão possui uma referência de acessório órfã: " +
                        normalized;
                    return false;
                }
            }

            return true;
        }

        private bool IsModernTruckAssignedToAiDriver(
            string[] lines,
            string truckId)
        {
            string normalizedTruck =
                NormalizeModernReference(truckId);

            foreach (string unitType in new string[]
                     {
                         "driver",
                         "driver_ai"
                     })
            {
                foreach (string driverId in GetModernUnitIds(
                             lines,
                             unitType))
                {
                    foreach (string field in new string[]
                             {
                                 "assigned_truck",
                                 "assigned_vehicle",
                                 "truck",
                                 "vehicle"
                             })
                    {
                        string rawValue;

                        if (TryGetModernUnitField(
                                lines,
                                unitType,
                                driverId,
                                field,
                                out rawValue) &&
                            NormalizeModernReference(rawValue)
                                .Equals(
                                    normalizedTruck,
                                    StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool IsModernTruckInDrivenGarageSlot(
            string[] lines,
            string truckId)
        {
            string normalizedTruck =
                NormalizeModernReference(truckId);

            foreach (string garageId in GetModernUnitIds(
                         lines,
                         "garage"))
            {
                Dictionary<int, string> vehicles =
                    GetModernUnitArrayEntries(
                        lines,
                        "garage",
                        garageId,
                        "vehicles");

                Dictionary<int, string> drivers =
                    GetModernUnitArrayEntries(
                        lines,
                        "garage",
                        garageId,
                        "drivers");

                foreach (KeyValuePair<int, string> vehicle
                         in vehicles)
                {
                    if (!NormalizeModernReference(vehicle.Value)
                            .Equals(
                                normalizedTruck,
                                StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string driverRaw;
                    if (drivers.TryGetValue(
                            vehicle.Key,
                            out driverRaw) &&
                        !String.IsNullOrWhiteSpace(
                            NormalizeModernReference(driverRaw)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool HasModernActiveJob(
            string[] lines,
            string playerId)
        {
            string currentJob;

            return TryGetModernUnitField(
                       lines,
                       "player",
                       playerId,
                       "current_job",
                       out currentJob) &&
                   !String.IsNullOrWhiteSpace(
                       NormalizeModernReference(currentJob));
        }

        private bool TryBuildModernTruckSwitchPlan(
            string targetTruckId,
            out ModernTruckSwitchPlan plan,
            out string reason)
        {
            plan = null;
            reason = "";

            if (!IsModernCompatibilityMode())
            {
                reason =
                    "A troca H5 é exclusiva do modo de compatibilidade moderno.";
                return false;
            }

            if (tempSavefileInMemory == null ||
                tempSavefileInMemory.Length == 0)
            {
                reason =
                    "O save decodificado não está disponível. Recarregue o save.";
                return false;
            }

            ResolveModernActiveVehicleReferences();

            string target =
                NormalizeModernReference(targetTruckId);

            if (String.IsNullOrWhiteSpace(target))
            {
                reason = "Selecione um caminhão válido.";
                return false;
            }

            if (target.Equals(
                    ModernActiveTruckId,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "Este caminhão já é o caminhão atual.";
                return false;
            }

            if (String.IsNullOrWhiteSpace(
                    ModernActivePlayerVehicleSlotId))
            {
                reason =
                    "O slot player_vehicles apontado por assigned_vehicles não pôde ser resolvido.";
                return false;
            }

            string playerId;
            if (!TryGetFirstModernUnitId(
                    tempSavefileInMemory,
                    "player",
                    out playerId))
            {
                reason = "O bloco player não foi encontrado.";
                return false;
            }

            if (HasModernActiveJob(
                    tempSavefileInMemory,
                    playerId))
            {
                reason =
                    "A troca fica bloqueada durante uma entrega ativa. " +
                    "Finalize ou cancele o trabalho e crie outro save.";
                return false;
            }

            List<string> ownedTrucks =
                GetModernUnitArrayValues(
                    tempSavefileInMemory,
                    "player",
                    playerId,
                    "trucks");

            if (!ownedTrucks.Any(x =>
                    x.Equals(
                        target,
                        StringComparison.OrdinalIgnoreCase)))
            {
                reason =
                    "O caminhão selecionado não está em player.trucks.";
                return false;
            }

            string graphReason;
            if (!ModernTruckHasValidAccessoryGraph(
                    ModernActiveTruckId,
                    out graphReason))
            {
                reason =
                    "O caminhão atual não passou na validação: " +
                    graphReason;
                return false;
            }

            if (!ModernTruckHasValidAccessoryGraph(
                    target,
                    out graphReason))
            {
                reason = graphReason;
                return false;
            }

            if (UserDriverDictionary == null ||
                GaragesList == null)
            {
                reason =
                    "Os dados de motoristas e garagens não estão disponíveis. " +
                    "Recarregue o save antes de tentar a troca.";
                return false;
            }

            string playerDriverId = "";

            if (SiiNunitData != null &&
                SiiNunitData.Player != null &&
                SiiNunitData.Player.drivers != null &&
                SiiNunitData.Player.drivers.Count > 0)
            {
                playerDriverId =
                    NormalizeModernReference(
                        SiiNunitData.Player.drivers[0]);
            }

            bool targetAssignedToAiDriver =
                UserDriverDictionary.Any(driverEntry =>
                    driverEntry.Value != null &&
                    !String.IsNullOrWhiteSpace(
                        driverEntry.Value.AssignedTruck) &&
                    NormalizeModernReference(
                        driverEntry.Value.AssignedTruck)
                        .Equals(
                            target,
                            StringComparison.OrdinalIgnoreCase) &&
                    (String.IsNullOrWhiteSpace(playerDriverId) ||
                     !NormalizeModernReference(driverEntry.Key)
                        .Equals(
                            playerDriverId,
                            StringComparison.OrdinalIgnoreCase)));

            if (!targetAssignedToAiDriver)
            {
                foreach (Garages garage in GaragesList)
                {
                    if (garage == null ||
                        garage.Vehicles == null ||
                        garage.Drivers == null)
                    {
                        continue;
                    }

                    for (int slotIndex = 0;
                         slotIndex < garage.Vehicles.Count;
                         slotIndex++)
                    {
                        if (!NormalizeModernReference(
                                garage.Vehicles[slotIndex])
                                .Equals(
                                    target,
                                    StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string garageDriverId =
                            slotIndex < garage.Drivers.Count
                                ? NormalizeModernReference(
                                    garage.Drivers[slotIndex])
                                : "";

                        if (String.IsNullOrWhiteSpace(garageDriverId))
                            continue;

                        if (!String.IsNullOrWhiteSpace(playerDriverId) &&
                            garageDriverId.Equals(
                                playerDriverId,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            // The original TS SE Tool uses this garage pairing
                            // only to display the player name. The modern active
                            // truck is resolved from player.assigned_vehicles.
                            continue;
                        }

                        targetAssignedToAiDriver = true;
                        break;
                    }

                    if (targetAssignedToAiDriver)
                        break;
                }
            }

            if (targetAssignedToAiDriver)
            {
                reason =
                    "O caminhão selecionado está atribuído a um motorista de IA. " +
                    "A troca com motoristas permanece bloqueada.";
                return false;
            }

            List<ModernPlayerVehicleSlotInfo> slots =
                GetModernPlayerVehicleSlots(
                    tempSavefileInMemory);

            ModernPlayerVehicleSlotInfo sourceSlot =
                slots.SingleOrDefault(x =>
                    x.SlotId.Equals(
                        ModernActivePlayerVehicleSlotId,
                        StringComparison.OrdinalIgnoreCase));

            if (sourceSlot == null ||
                !sourceSlot.VehicleId.Equals(
                    ModernActiveTruckId,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason =
                    "O slot ativo não aponta para o caminhão atual. " +
                    "Nenhuma alteração será permitida.";
                return false;
            }

            List<string> inventorySlotIds =
                GetModernUnitArrayValues(
                    tempSavefileInMemory,
                    "player",
                    playerId,
                    "my_vehicles");

            List<ModernPlayerVehicleSlotInfo> currentInventorySlots =
                slots.Where(x =>
                        !x.SlotId.Equals(
                            ModernActivePlayerVehicleSlotId,
                            StringComparison.OrdinalIgnoreCase) &&
                        x.VehicleId.Equals(
                            ModernActiveTruckId,
                            StringComparison.OrdinalIgnoreCase) &&
                        inventorySlotIds.Any(slotId =>
                            slotId.Equals(
                                x.SlotId,
                                StringComparison.OrdinalIgnoreCase)))
                    .ToList();

            List<ModernPlayerVehicleSlotInfo> targetSlots =
                slots.Where(x =>
                        x.VehicleId.Equals(
                            target,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            List<ModernPlayerVehicleSlotInfo> targetInventorySlots =
                targetSlots.Where(x =>
                        inventorySlotIds.Any(slotId =>
                            slotId.Equals(
                                x.SlotId,
                                StringComparison.OrdinalIgnoreCase)))
                    .ToList();

            bool preserveCurrentInventory =
                currentInventorySlots.Count == 1 &&
                targetSlots.Count == 0;

            bool swapInventorySlot =
                currentInventorySlots.Count == 0 &&
                targetSlots.Count == 1 &&
                targetInventorySlots.Count == 1;

            if (!preserveCurrentInventory &&
                !swapInventorySlot)
            {
                reason =
                    "A topologia de inventário não está entre os dois cenários " +
                    "homologados pela H5." +
                    Environment.NewLine +
                    "Atual no inventário: " +
                    currentInventorySlots.Count.ToString(
                        CultureInfo.InvariantCulture) +
                    "; destino em player_vehicles: " +
                    targetSlots.Count.ToString(
                        CultureInfo.InvariantCulture) +
                    "; destino no inventário: " +
                    targetInventorySlots.Count.ToString(
                        CultureInfo.InvariantCulture) +
                    "." +
                    Environment.NewLine +
                    "Nenhuma alteração será permitida.";
                return false;
            }

            ModernPlayerVehicleSlotInfo inventorySlot =
                preserveCurrentInventory
                    ? currentInventorySlots[0]
                    : targetInventorySlots[0];

            if (!String.IsNullOrWhiteSpace(
                    inventorySlot.TrailerId))
            {
                reason =
                    "O slot de inventário envolvido na troca possui um reboque. " +
                    "A H5 não altera esse tipo de configuração.";
                return false;
            }

            string sourceTrailerRaw;
            if (!TryGetModernUnitField(
                    tempSavefileInMemory,
                    "player_vehicles",
                    sourceSlot.SlotId,
                    "trailer",
                    out sourceTrailerRaw))
            {
                reason =
                    "O campo trailer do slot ativo não foi encontrado.";
                return false;
            }

            plan = new ModernTruckSwitchPlan();
            plan.PlayerId = playerId;
            plan.PreviousTruckId = ModernActiveTruckId;
            plan.TargetTruckId = target;
            plan.SourceSlotId = sourceSlot.SlotId;
            plan.SourceTrailerId =
                NormalizeModernReference(sourceTrailerRaw);

            if (preserveCurrentInventory)
            {
                plan.PreviousInventorySlotId =
                    inventorySlot.SlotId;
            }
            else
            {
                plan.TargetSlotId =
                    inventorySlot.SlotId;
                plan.TargetTrailerId =
                    inventorySlot.TrailerId;
            }

            return true;
        }

        private bool HasUnsavedModernVehicleChanges()
        {
            if (!ModernVehicleBaselineIsCurrent())
                return true;

            if (!String.IsNullOrWhiteSpace(
                    ModernActiveTruckId) &&
                ModernTruckBaseline != null &&
                IsModernTruckObject(ModernActiveTruckId))
            {
                ModernVehicleSnapshot current =
                    CaptureModernTruckSnapshot(
                        (Vehicle)SiiNunitData.SiiNitems[
                            ModernActiveTruckId]);

                if (GetChangedModernScalars(
                        ModernTruckBaseline,
                        current).Count > 0 ||
                    GetChangedModernArrays(
                        ModernTruckBaseline,
                        current).Count > 0)
                {
                    return true;
                }
            }

            foreach (string trailerId in ModernTrailerChainIds)
            {
                ModernVehicleSnapshot baseline;

                if (!ModernTrailerBaselines.TryGetValue(
                        trailerId,
                        out baseline) ||
                    !IsModernTrailerObject(trailerId))
                {
                    continue;
                }

                ModernVehicleSnapshot current =
                    CaptureModernTrailerSnapshot(
                        (Trailer)SiiNunitData.SiiNitems[
                            trailerId]);

                if (GetChangedModernScalars(
                        baseline,
                        current).Count > 0 ||
                    GetChangedModernArrays(
                        baseline,
                        current).Count > 0)
                {
                    return true;
                }
            }

            return HasUnsavedModernFleetChangesH6();
        }

        private string GetSelectedModernTruckId()
        {
            if (comboBoxUserTruckCompanyTrucks == null ||
                comboBoxUserTruckCompanyTrucks.SelectedValue == null)
            {
                return "";
            }

            return NormalizeModernReference(
                comboBoxUserTruckCompanyTrucks
                    .SelectedValue
                    .ToString());
        }

        private void SetModernTruckEditingEnabled(
            bool enabled)
        {
            groupBoxUserTruckTruckDetails.Enabled = enabled;

            if (ModernTruckAdvancedPanel != null)
                ModernTruckAdvancedPanel.Enabled = enabled;

            foreach (string controlName in new string[]
                     {
                         "buttonTruckRepair",
                         "buttonTruckReFuel"
                     })
            {
                Control control =
                    tabPageTruck.Controls
                        .Find(controlName, true)
                        .FirstOrDefault();

                if (control != null)
                    control.Enabled = enabled;
            }
        }

        private void RefreshModernActiveTruckDisplayH5()
        {
            if (!IsModernCompatibilityMode() ||
                comboBoxUserTruckCompanyTrucks == null ||
                String.IsNullOrWhiteSpace(ModernActiveTruckId))
            {
                return;
            }

            System.Data.DataTable truckTable =
                comboBoxUserTruckCompanyTrucks.DataSource
                    as System.Data.DataTable;

            if (truckTable == null ||
                !truckTable.Columns.Contains("UserTruckNameless") ||
                !truckTable.Columns.Contains("DriverName"))
            {
                return;
            }

            string profileName =
                TS_SE_Tool.Utilities.TextUtilities.FromHexToString(
                    Globals.SelectedProfile);

            foreach (System.Data.DataRow row in truckTable.Rows)
            {
                string rowTruckId =
                    NormalizeModernReference(
                        Convert.ToString(
                            row["UserTruckNameless"],
                            CultureInfo.InvariantCulture));

                string driverText =
                    row.IsNull("DriverName")
                        ? ""
                        : Convert.ToString(
                            row["DriverName"],
                            CultureInfo.InvariantCulture);

                if (driverText.StartsWith(
                        "> ",
                        StringComparison.Ordinal))
                {
                    row["DriverName"] = "null";
                }

                if (rowTruckId.Equals(
                        ModernActiveTruckId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    row["DriverName"] =
                        "> " +
                        (String.IsNullOrWhiteSpace(profileName)
                            ? "Player"
                            : profileName);
                }
            }

            comboBoxUserTruckCompanyTrucks.Refresh();

            IO_Utilities.LogWriter(
                "RC5.1H5 active truck display synchronized from assigned_vehicles: " +
                ModernActiveTruckId);
        }
        private void UpdateModernTruckSwitchUi()
        {
            if (!IsModernCompatibilityMode() ||
                comboBoxUserTruckCompanyTrucks == null)
            {
                return;
            }

            bool pending =
                !String.IsNullOrWhiteSpace(
                    ModernPendingTruckSwitchTargetTruckId);

            comboBoxUserTruckCompanyTrucks.Enabled =
                !pending;
            buttonUserTruckSelectCurrent.Enabled =
                !pending;

            string selectedTruck =
                GetSelectedModernTruckId();

            bool selectedIsActive =
                !String.IsNullOrWhiteSpace(selectedTruck) &&
                selectedTruck.Equals(
                    ModernActiveTruckId,
                    StringComparison.OrdinalIgnoreCase);

            // RC5.1H1: do not run the complete save-graph validation while
            // loading the form or merely browsing the truck combo. The full
            // validation still runs synchronously when the user clicks the
            // controlled-switch button.
            bool canRequestValidation =
                !pending &&
                !String.IsNullOrWhiteSpace(selectedTruck) &&
                !selectedIsActive;

            buttonUserTruckSwitchCurrent.Text =
                pending
                    ? "Troca preparada — clique em Salvar"
                    : "Definir como caminhão atual (H5)";

            buttonUserTruckSwitchCurrent.Enabled =
                canRequestValidation;

            SetModernTruckEditingEnabled(
                !pending &&
                selectedIsActive &&
                ModernActiveTruckEditingAvailable);

            if (pending)
            {
                toolTipMain.SetToolTip(
                    buttonUserTruckSwitchCurrent,
                    "A troca foi preparada. Salve e recarregue o save.");
            }
            else if (selectedIsActive)
            {
                toolTipMain.SetToolTip(
                    buttonUserTruckSwitchCurrent,
                    "Este caminhão já é o caminhão atual.");
            }
            else if (canRequestValidation)
            {
                toolTipMain.SetToolTip(
                    buttonUserTruckSwitchCurrent,
                    "A validação completa será executada ao clicar.");
            }
            else
            {
                toolTipMain.SetToolTip(
                    buttonUserTruckSwitchCurrent,
                    "Selecione um caminhão válido.");
            }
        }

        private void ModernTruckSelectionChanged_RC5(
            object sender,
            EventArgs e)
        {
            if (!IsModernCompatibilityMode())
                return;

            UpdateModernTruckSwitchUi();
            UpdateModernTruckFleetEditUiH6();
        }

        private void ModernTruckSwitchCurrent_RC5(
            object sender,
            EventArgs e)
        {
            if (!IsModernCompatibilityMode())
                return;

            string targetTruckId =
                GetSelectedModernTruckId();

            ModernTruckSwitchPlan plan;
            string reason;

            if (!TryBuildModernTruckSwitchPlan(
                    targetTruckId,
                    out plan,
                    out reason))
            {
                MessageBox.Show(
                    reason,
                    "H5 — troca de caminhão bloqueada",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                UpdateModernTruckSwitchUi();
                return;
            }

            if (HasUnsavedModernVehicleChanges())
            {
                MessageBox.Show(
                    "Existem alterações de reparo, combustível, placa ou " +
                    "dados avançados ainda não gravadas." +
                    Environment.NewLine +
                    "Salve, recarregue o save e só então prepare a troca.",
                    "H5 — salve as alterações primeiro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string selectedDescription =
                comboBoxUserTruckCompanyTrucks.Text;

            string inventoryAction =
                String.IsNullOrWhiteSpace(plan.TargetSlotId)
                    ? "O caminhão anterior continuará no slot de inventário já existente."
                    : "O caminhão anterior ocupará o slot de inventário do caminhão selecionado.";

            DialogResult confirmation = MessageBox.Show(
                "Caminhão atual:" +
                Environment.NewLine +
                plan.PreviousTruckId +
                Environment.NewLine +
                Environment.NewLine +
                "Novo caminhão:" +
                Environment.NewLine +
                selectedDescription +
                Environment.NewLine +
                plan.TargetTruckId +
                Environment.NewLine +
                Environment.NewLine +
                "O reboque atualmente acoplado permanecerá no slot ativo." +
                Environment.NewLine +
                inventoryAction +
                Environment.NewLine +
                Environment.NewLine +
                "Preparar esta troca?",
                "H5 — confirmar troca controlada",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirmation != DialogResult.Yes)
                return;

            ModernPendingTruckSwitchPreviousTruckId =
                plan.PreviousTruckId;
            ModernPendingTruckSwitchTargetTruckId =
                plan.TargetTruckId;
            ModernPendingTruckSwitchSourceSlotId =
                plan.SourceSlotId;
            ModernPendingTruckSwitchTargetSlotId =
                plan.TargetSlotId;
            ModernPendingTruckSwitchPreviousInventorySlotId =
                plan.PreviousInventorySlotId;

            IO_Utilities.LogWriter(
                "H5 staged controlled truck switch: " +
                plan.PreviousTruckId +
                " -> " +
                plan.TargetTruckId +
                ", active slot=" +
                plan.SourceSlotId +
                ", inventory mode=" +
                (String.IsNullOrWhiteSpace(plan.TargetSlotId)
                    ? "preserve " + plan.PreviousInventorySlotId
                    : "swap " + plan.TargetSlotId));

            UpdateModernTruckSwitchUi();
            tabPageTrailer.Enabled = false;

            MessageBox.Show(
                "A troca foi preparada em memória." +
                Environment.NewLine +
                "Clique em Salvar para aplicar." +
                Environment.NewLine +
                "Depois recarregue o save antes de fazer outra edição.",
                "H5 — troca preparada",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void AttachModernTruckSwitchEvents()
        {
            if (ModernTruckSwitchEventsAttached)
                return;

            buttonUserTruckSwitchCurrent.Click -=
                buttonUserTruckSwitchCurrent_Click;
            buttonUserTruckSwitchCurrent.Click +=
                ModernTruckSwitchCurrent_RC5;

            comboBoxUserTruckCompanyTrucks.SelectedIndexChanged +=
                ModernTruckSelectionChanged_RC5;

            ModernTruckSwitchEventsAttached = true;
        }

        private int CountModernPlayerVehicleReferences(
            string[] lines,
            string truckId)
        {
            string normalized =
                NormalizeModernReference(truckId);

            return GetModernPlayerVehicleSlots(lines)
                .Count(x =>
                    x.VehicleId.Equals(
                        normalized,
                        StringComparison.OrdinalIgnoreCase));
        }

        private string PatchModernTruckSwitchData(
            string content)
        {
            if (String.IsNullOrWhiteSpace(
                    ModernPendingTruckSwitchTargetTruckId))
            {
                return content;
            }

            bool swapInventorySlot =
                !String.IsNullOrWhiteSpace(
                    ModernPendingTruckSwitchTargetSlotId);

            bool preserveCurrentInventory =
                !String.IsNullOrWhiteSpace(
                    ModernPendingTruckSwitchPreviousInventorySlotId);

            if (swapInventorySlot == preserveCurrentInventory)
            {
                throw new InvalidDataException(
                    "H5 detected an invalid staged inventory mode.");
            }

            string secondarySlotId =
                swapInventorySlot
                    ? ModernPendingTruckSwitchTargetSlotId
                    : ModernPendingTruckSwitchPreviousInventorySlotId;

            string[] beforeLines = content
                .Replace("\r\n", "\n")
                .Split(new char[] { '\n' });

            string sourceVehicleRaw;
            string sourceTrailerRaw;
            string secondaryVehicleRaw;
            string secondaryTrailerRaw;

            if (!TryGetModernUnitField(
                    beforeLines,
                    "player_vehicles",
                    ModernPendingTruckSwitchSourceSlotId,
                    "vehicle",
                    out sourceVehicleRaw) ||
                !TryGetModernUnitField(
                    beforeLines,
                    "player_vehicles",
                    ModernPendingTruckSwitchSourceSlotId,
                    "trailer",
                    out sourceTrailerRaw) ||
                !TryGetModernUnitField(
                    beforeLines,
                    "player_vehicles",
                    secondarySlotId,
                    "vehicle",
                    out secondaryVehicleRaw) ||
                !TryGetModernUnitField(
                    beforeLines,
                    "player_vehicles",
                    secondarySlotId,
                    "trailer",
                    out secondaryTrailerRaw))
            {
                throw new InvalidDataException(
                    "H5 could not revalidate the active and inventory slots.");
            }

            string expectedSecondaryVehicleBefore =
                swapInventorySlot
                    ? ModernPendingTruckSwitchTargetTruckId
                    : ModernPendingTruckSwitchPreviousTruckId;

            if (!NormalizeModernReference(sourceVehicleRaw)
                    .Equals(
                        ModernPendingTruckSwitchPreviousTruckId,
                        StringComparison.OrdinalIgnoreCase) ||
                !NormalizeModernReference(secondaryVehicleRaw)
                    .Equals(
                        expectedSecondaryVehicleBefore,
                        StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "H5 detected a changed truck assignment. " +
                    "Reload the save before trying again.");
            }

            if (!String.IsNullOrWhiteSpace(
                    NormalizeModernReference(secondaryTrailerRaw)))
            {
                throw new InvalidDataException(
                    "H5 refused the switch because the inventory slot " +
                    "now contains a trailer.");
            }

            string playerId;
            if (!TryGetFirstModernUnitId(
                    beforeLines,
                    "player",
                    out playerId))
            {
                throw new InvalidDataException(
                    "H5 could not locate the player block.");
            }

            string assignedVehiclesBefore;
            if (!TryGetModernUnitField(
                    beforeLines,
                    "player",
                    playerId,
                    "assigned_vehicles",
                    out assignedVehiclesBefore) ||
                !NormalizeModernReference(assignedVehiclesBefore)
                    .Equals(
                        ModernPendingTruckSwitchSourceSlotId,
                        StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "H5 detected a changed assigned_vehicles pointer.");
            }

            List<string> ownedTrucksBefore =
                GetModernUnitArrayValues(
                    beforeLines,
                    "player",
                    playerId,
                    "trucks");

            List<string> inventorySlotsBefore =
                GetModernUnitArrayValues(
                    beforeLines,
                    "player",
                    playerId,
                    "my_vehicles");

            if (!ownedTrucksBefore.Any(x =>
                    x.Equals(
                        ModernPendingTruckSwitchPreviousTruckId,
                        StringComparison.OrdinalIgnoreCase)) ||
                !ownedTrucksBefore.Any(x =>
                    x.Equals(
                        ModernPendingTruckSwitchTargetTruckId,
                        StringComparison.OrdinalIgnoreCase)) ||
                !inventorySlotsBefore.Any(x =>
                    x.Equals(
                        secondarySlotId,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "H5 detected an invalid ownership or inventory topology.");
            }

            int previousReferencesBefore =
                CountModernPlayerVehicleReferences(
                    beforeLines,
                    ModernPendingTruckSwitchPreviousTruckId);

            int targetReferencesBefore =
                CountModernPlayerVehicleReferences(
                    beforeLines,
                    ModernPendingTruckSwitchTargetTruckId);

            if ((preserveCurrentInventory &&
                 (previousReferencesBefore != 2 ||
                  targetReferencesBefore != 0)) ||
                (swapInventorySlot &&
                 (previousReferencesBefore != 1 ||
                  targetReferencesBefore != 1)))
            {
                throw new InvalidDataException(
                    "H5 detected unexpected player_vehicles reference counts.");
            }

            string output = PatchModernUnitFields(
                content,
                "player_vehicles",
                ModernPendingTruckSwitchSourceSlotId,
                new Dictionary<string, string>(
                    StringComparer.Ordinal)
                {
                    {
                        "vehicle",
                        ModernPendingTruckSwitchTargetTruckId
                    }
                },
                null);

            if (swapInventorySlot)
            {
                output = PatchModernUnitFields(
                    output,
                    "player_vehicles",
                    ModernPendingTruckSwitchTargetSlotId,
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        {
                            "vehicle",
                            ModernPendingTruckSwitchPreviousTruckId
                        }
                    },
                    null);
            }

            string[] intermediateLines = output
                .Replace("\r\n", "\n")
                .Split(new char[] { '\n' });

            foreach (string legacyField in new string[]
                     {
                         "assigned_truck",
                         "my_truck"
                     })
            {
                string legacyValue;

                if (!TryGetModernUnitField(
                        intermediateLines,
                        "player",
                        playerId,
                        legacyField,
                        out legacyValue) ||
                    !NormalizeModernReference(legacyValue)
                        .Equals(
                            ModernPendingTruckSwitchPreviousTruckId,
                            StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                output = PatchModernUnitFields(
                    output,
                    "player",
                    playerId,
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        {
                            legacyField,
                            ModernPendingTruckSwitchTargetTruckId
                        }
                    },
                    null);

                intermediateLines = output
                    .Replace("\r\n", "\n")
                    .Split(new char[] { '\n' });
            }

            string[] afterLines = output
                .Replace("\r\n", "\n")
                .Split(new char[] { '\n' });

            string sourceVehicleAfter = "";
            string sourceTrailerAfter = "";
            string secondaryVehicleAfter = "";
            string secondaryTrailerAfter = "";
            string assignedVehiclesAfter = "";

            bool valid =
                TryGetModernUnitField(
                    afterLines,
                    "player_vehicles",
                    ModernPendingTruckSwitchSourceSlotId,
                    "vehicle",
                    out sourceVehicleAfter) &&
                TryGetModernUnitField(
                    afterLines,
                    "player_vehicles",
                    ModernPendingTruckSwitchSourceSlotId,
                    "trailer",
                    out sourceTrailerAfter) &&
                TryGetModernUnitField(
                    afterLines,
                    "player_vehicles",
                    secondarySlotId,
                    "vehicle",
                    out secondaryVehicleAfter) &&
                TryGetModernUnitField(
                    afterLines,
                    "player_vehicles",
                    secondarySlotId,
                    "trailer",
                    out secondaryTrailerAfter) &&
                TryGetModernUnitField(
                    afterLines,
                    "player",
                    playerId,
                    "assigned_vehicles",
                    out assignedVehiclesAfter);

            List<string> ownedTrucksAfter =
                GetModernUnitArrayValues(
                    afterLines,
                    "player",
                    playerId,
                    "trucks");

            List<string> inventorySlotsAfter =
                GetModernUnitArrayValues(
                    afterLines,
                    "player",
                    playerId,
                    "my_vehicles");

            if (!valid ||
                !NormalizeModernReference(sourceVehicleAfter)
                    .Equals(
                        ModernPendingTruckSwitchTargetTruckId,
                        StringComparison.OrdinalIgnoreCase) ||
                !NormalizeModernReference(secondaryVehicleAfter)
                    .Equals(
                        ModernPendingTruckSwitchPreviousTruckId,
                        StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(
                    sourceTrailerRaw.Trim(),
                    sourceTrailerAfter.Trim(),
                    StringComparison.Ordinal) ||
                !String.Equals(
                    secondaryTrailerRaw.Trim(),
                    secondaryTrailerAfter.Trim(),
                    StringComparison.Ordinal) ||
                !NormalizeModernReference(assignedVehiclesAfter)
                    .Equals(
                        ModernPendingTruckSwitchSourceSlotId,
                        StringComparison.OrdinalIgnoreCase) ||
                CountModernPlayerVehicleReferences(
                    afterLines,
                    ModernPendingTruckSwitchTargetTruckId) != 1 ||
                CountModernPlayerVehicleReferences(
                    afterLines,
                    ModernPendingTruckSwitchPreviousTruckId) != 1 ||
                !ownedTrucksBefore.SequenceEqual(
                    ownedTrucksAfter,
                    StringComparer.OrdinalIgnoreCase) ||
                !inventorySlotsBefore.SequenceEqual(
                    inventorySlotsAfter,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "H5 post-write validation rejected the truck switch.");
            }

            IO_Utilities.LogWriter(
                "RC5.1H5 inventory-aware truck switch validated: " +
                ModernPendingTruckSwitchPreviousTruckId +
                " -> " +
                ModernPendingTruckSwitchTargetTruckId +
                ", active slot=" +
                ModernPendingTruckSwitchSourceSlotId +
                ", inventory mode=" +
                (swapInventorySlot
                    ? "swap target slot " +
                      ModernPendingTruckSwitchTargetSlotId
                    : "preserve current slot " +
                      ModernPendingTruckSwitchPreviousInventorySlotId));

            return output;
        }

        private void EnsureModernActiveVehicleDictionaryEntries()
        {
            // The original TS SE Tool builds its trailer list only from
            // player.trailers and player_job.company_trailer. ETS2 1.60 may
            // expose the attached trailer only through player_vehicles.
            // This bridge affects UI wrapper dictionaries only; it never
            // rewrites player, garage or ownership references.

            if (!String.IsNullOrWhiteSpace(ModernActiveTruckId) &&
                IsModernTruckObject(ModernActiveTruckId) &&
                UserTruckDictionary != null)
            {
                UserCompanyTruckData truckEntry;

                if (!UserTruckDictionary.TryGetValue(
                        ModernActiveTruckId,
                        out truckEntry))
                {
                    truckEntry = new UserCompanyTruckData();
                    truckEntry.Users = false;
                    UserTruckDictionary.Add(
                        ModernActiveTruckId,
                        truckEntry);
                }

                truckEntry.TruckMainData =
                    (Vehicle)SiiNunitData.SiiNitems[
                        ModernActiveTruckId];
            }

            if (!String.IsNullOrWhiteSpace(ModernActiveTrailerId) &&
                IsModernTrailerObject(ModernActiveTrailerId) &&
                UserTrailerDictionary != null)
            {
                UserCompanyTrailerData trailerEntry;

                if (!UserTrailerDictionary.TryGetValue(
                        ModernActiveTrailerId,
                        out trailerEntry))
                {
                    trailerEntry = new UserCompanyTrailerData();
                    UserTrailerDictionary.Add(
                        ModernActiveTrailerId,
                        trailerEntry);
                }

                trailerEntry.Main = true;

                // FillUserCompanyTrailerList excludes an owned trailer when
                // it is currently attached and therefore absent from a
                // garage slot. Marking only the UI wrapper as non-garaged
                // keeps it visible without changing ownership in the save.
                trailerEntry.Users = false;
                trailerEntry.TrailerMainData =
                    (Trailer)SiiNunitData.SiiNitems[
                        ModernActiveTrailerId];

                IO_Utilities.LogWriter(
                    "RC5 added active trailer to the compatibility UI: " +
                    ModernActiveTrailerId);
            }
        }

        private string GetSelectedModernTrailerIdH2()
        {
            if (comboBoxUserTrailerCompanyTrailers == null ||
                comboBoxUserTrailerCompanyTrailers.SelectedValue == null)
            {
                return "";
            }

            return NormalizeModernReference(
                comboBoxUserTrailerCompanyTrailers
                    .SelectedValue
                    .ToString());
        }

        private void ApplyModernTrailerEditPermissionsH2(
            bool selectedIsActive,
            bool detailsAvailable)
        {
            string selectedTrailerId =
                GetSelectedModernTrailerIdH6();

            bool allowEditing =
                detailsAvailable &&
                (selectedIsActive ||
                 IsOwnedModernTrailerH6(selectedTrailerId));

            if (ModernTrailerAdvancedPanel != null)
                ModernTrailerAdvancedPanel.Enabled = allowEditing;

            Control trailerInfo =
                tabPageTrailer.Controls
                    .Find("buttonTrailerInfo", true)
                    .FirstOrDefault();

            if (trailerInfo != null)
                trailerInfo.Enabled = false;

            Control licensePlateEdit =
                tabPageTrailer.Controls
                    .Find("buttonTrailerLicensePlateEdit", true)
                    .FirstOrDefault();

            if (licensePlateEdit != null)
                licensePlateEdit.Enabled = allowEditing;

            buttonUserTrailerSwitchCurrent.Enabled = false;

            if (!allowEditing)
            {
                Control repairTrailer =
                    tabPageTrailer.Controls
                        .Find("buttonTrailerRepair", true)
                        .FirstOrDefault();

                if (repairTrailer != null)
                    repairTrailer.Enabled = false;

                for (int repairIndex = 0;
                     repairIndex < 4;
                     repairIndex++)
                {
                    Control repairPart =
                        tabPageTrailer.Controls
                            .Find(
                                "buttonTrailerElRepair" +
                                repairIndex.ToString(
                                    CultureInfo.InvariantCulture),
                                true)
                            .FirstOrDefault();

                    if (repairPart != null)
                        repairPart.Enabled = false;
                }
            }
        }

        private void UpdateModernTrailerBrowseUiH2()
        {
            if (!IsModernCompatibilityMode() ||
                ModernTrailerUiRefreshingH2)
            {
                return;
            }

            ModernTrailerUiRefreshingH2 = true;

            try
            {
                tabPageTrailer.Enabled = true;

                bool hasOwnedTrailerRows =
                    comboBoxUserTrailerCompanyTrailers != null &&
                    comboBoxUserTrailerCompanyTrailers.Items.Count > 1 &&
                    UserTrailerDictionary != null &&
                    UserTrailerDictionary.Count > 0;

                comboBoxUserTrailerCompanyTrailers.Enabled =
                    hasOwnedTrailerRows;

                buttonUserTrailerSwitchCurrent.Enabled = false;

                bool activeTrailerAvailable =
                    !String.IsNullOrWhiteSpace(ModernActiveTrailerId) &&
                    UserTrailerDictionary != null &&
                    UserTrailerDictionary.ContainsKey(
                        ModernActiveTrailerId);

                buttonUserTrailerSelectCurrent.Enabled =
                    activeTrailerAvailable;

                if (!hasOwnedTrailerRows)
                {
                    ToggleTrailerPartsCondition(false);
                    groupBoxUserTrailerTrailerDetails.Enabled = false;
                    groupBoxUserTrailerShareTrailerSettings.Enabled = false;
                    tableLayoutPanelUserTrailerControls.Enabled = false;
                    ApplyModernTrailerEditPermissionsH2(false, false);

                    toolTipMain.SetToolTip(
                        comboBoxUserTrailerCompanyTrailers,
                        "Nenhum reboque próprio foi encontrado neste save.");

                    return;
                }

                string selectedTrailer =
                    GetSelectedModernTrailerIdH2();

                if (String.IsNullOrWhiteSpace(selectedTrailer) &&
                    comboBoxUserTrailerCompanyTrailers.Items.Count > 1)
                {
                    // Row zero is the original "-- NONE --" item.
                    comboBoxUserTrailerCompanyTrailers.SelectedIndex = 1;
                    selectedTrailer =
                        GetSelectedModernTrailerIdH2();
                }

                bool selectedTrailerAvailable =
                    !String.IsNullOrWhiteSpace(selectedTrailer) &&
                    UserTrailerDictionary.ContainsKey(selectedTrailer);

                ToggleTrailerPartsCondition(
                    selectedTrailerAvailable);

                groupBoxUserTrailerTrailerDetails.Enabled =
                    selectedTrailerAvailable;
                groupBoxUserTrailerShareTrailerSettings.Enabled =
                    selectedTrailerAvailable;
                tableLayoutPanelUserTrailerControls.Enabled =
                    selectedTrailerAvailable;

                if (!selectedTrailerAvailable)
                {
                    ApplyModernTrailerEditPermissionsH2(false, false);
                    return;
                }

                bool detailsAvailable = true;

                try
                {
                    UpdateTrailerPanelDetails();
                }
                catch (Exception ex)
                {
                    detailsAvailable = false;

                    IO_Utilities.ErrorLogWriter(
                        "RC5.1H2 isolated a legacy Trailer detail UI error: " +
                        ex.Message +
                        Environment.NewLine +
                        ex.StackTrace);
                }

                bool selectedIsActive =
                    activeTrailerAvailable &&
                    selectedTrailer.Equals(
                        ModernActiveTrailerId,
                        StringComparison.OrdinalIgnoreCase);

                ApplyModernTrailerEditPermissionsH2(
                    selectedIsActive,
                    detailsAvailable);

                if (selectedIsActive)
                {
                    toolTipMain.SetToolTip(
                        comboBoxUserTrailerCompanyTrailers,
                        "Reboque principal acoplado. " +
                        "Os campos já homologados permanecem editáveis.");
                }
                else if (!activeTrailerAvailable)
                {
                    toolTipMain.SetToolTip(
                        comboBoxUserTrailerCompanyTrailers,
                        "Navegação pelos reboques próprios em modo de leitura. " +
                        "Acople um reboque no ETS2 para editar os campos homologados.");
                }
                else
                {
                    toolTipMain.SetToolTip(
                        comboBoxUserTrailerCompanyTrailers,
                        "Reboque próprio não ativo: visualização somente leitura.");
                }
            }
            finally
            {
                ModernTrailerUiRefreshingH2 = false;
            }
        }

        private void ModernTrailerSelectionChangedH2(
            object sender,
            EventArgs e)
        {
            UpdateModernTrailerBrowseUiH2();
            UpdateModernTrailerFleetEditUiH6();
        }

        private void AttachModernTrailerBrowseEventsH2()
        {
            if (ModernTrailerBrowseEventsAttachedH2)
                return;

            // Replace the legacy handler because it can enable write controls
            // for any selected trailer and can propagate modern-renderer errors.
            comboBoxUserTrailerCompanyTrailers.SelectedIndexChanged -=
                comboBoxCompanyTrailers_SelectedIndexChanged;
            comboBoxUserTrailerCompanyTrailers.SelectedIndexChanged -=
                ModernTrailerSelectionChangedH2;
            comboBoxUserTrailerCompanyTrailers.SelectedIndexChanged +=
                ModernTrailerSelectionChangedH2;

            ModernTrailerBrowseEventsAttachedH2 = true;
        }

        private void ConfigureModernReadOnlyControlsH3(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                // RC5.1H8.2 BEGIN: preserve H8 controls.
                if (String.Equals(
                        control.Name,
                        "groupBoxModernConvoyToolsH8",
                        StringComparison.Ordinal))
                {
                    SetModernConvoyControlTreeEnabledH82(control);
                    continue;
                }
                // RC5.1H8.2 END.
                if (control is GroupBox ||
                    control is Panel ||
                    control is TableLayoutPanel ||
                    control is FlowLayoutPanel ||
                    control is TabControl ||
                    control is TabPage)
                {
                    control.Enabled = true;
                }

                if (control.HasChildren)
                    ConfigureModernReadOnlyControlsH3(control);

                TextBoxBase textBox = control as TextBoxBase;
                if (textBox != null)
                {
                    textBox.ReadOnly = true;
                    textBox.Enabled = true;
                    textBox.TabStop = false;
                    continue;
                }

                if (control is CheckedListBox ||
                    control is ButtonBase ||
                    control is NumericUpDown ||
                    control is DateTimePicker)
                {
                    control.Enabled = false;
                    control.TabStop = false;
                    continue;
                }

                if (control is ComboBox ||
                    control is ListBox ||
                    control is TreeView ||
                    control is DataGridView)
                {
                    control.Enabled = true;
                    control.TabStop = true;
                    continue;
                }

            }
        }

        private void ApplyModernReadOnlyTabsH3()
        {
            if (!IsModernCompatibilityMode())
                return;

            for (int tabIndex = 4;
                 tabIndex < tabControlMain.TabPages.Count;
                 tabIndex++)
            {
                TabPage readOnlyTab =
                    tabControlMain.TabPages[tabIndex];

                readOnlyTab.Enabled = true;
                ConfigureModernReadOnlyControlsH3(readOnlyTab);
            }

            ToolStripItem[] mutationCommands =
            {
                contextMenuStripFreightMarketJobListEdit,
                contextMenuStripFreightMarketJobListDelete,
                contextMenuStripCompanyDriversEdit,
                contextMenuStripCompanyDriversHire,
                contextMenuStripCompanyDriversFire
            };

            foreach (ToolStripItem command in mutationCommands)
            {
                if (command != null)
                    command.Enabled = false;
            }

            IO_Utilities.LogWriter(
                "RC5.1H3 read-only Company and auxiliary tabs initialized.");
        }

        private void ApplyModernVehicleTabRestrictions()
        {
            if (!IsModernCompatibilityMode())
                return;

            ResolveModernActiveVehicleReferences();
            EnsureModernActiveVehicleDictionaryEntries();
            ConfigureModernAdvancedVehiclePanels(true);
            RefreshModernActiveTruckDisplayH5();

            // Keep the four already homologated compatibility tabs reachable.
            for (int safeTabIndex = 0;
                 safeTabIndex < tabControlMain.TabPages.Count &&
                 safeTabIndex <= 3;
                 safeTabIndex++)
            {
                tabControlMain.TabPages[safeTabIndex].Enabled = true;
            }

            bool truckReady =
                !String.IsNullOrWhiteSpace(ModernActiveTruckId) &&
                UserTruckDictionary != null &&
                UserTruckDictionary.ContainsKey(ModernActiveTruckId);

            tabPageTruck.Enabled = truckReady;

            if (truckReady)
            {
                ModernActiveTruckEditingAvailable = true;

                try
                {
                    comboBoxUserTruckCompanyTrucks.SelectedValue =
                        ModernActiveTruckId;
                    comboBoxUserTruckCompanyTrucks.Enabled = false;
                    buttonUserTruckSelectCurrent.Enabled = false;
                    buttonUserTruckSwitchCurrent.Enabled = false;
                    groupBoxUserTruckShareTruckSettings.Enabled = true;
                    tableLayoutPanelUserTruckControls.Enabled = true;
                    groupBoxUserTruckTruckDetails.Enabled = true;

                    Control vehicleEditor =
                        tabPageTruck.Controls
                            .Find("buttonTruckVehicleEditor", true)
                            .FirstOrDefault();

                    if (vehicleEditor != null)
                    {
                        vehicleEditor.Enabled =
                            CanOpenModernAccessoryPackageH19();
                    }

                    UpdateTruckPanelDetails();

                    toolTipMain.SetToolTip(
                        comboBoxUserTruckCompanyTrucks,
                        "RC5 active truck: " + ModernActiveTruckId);
                }
                catch (Exception ex)
                {
                    ModernActiveTruckEditingAvailable = false;
                    tabPageTruck.Enabled = true;
                    groupBoxUserTruckTruckDetails.Enabled = false;
                    tableLayoutPanelUserTruckControls.Enabled = false;

                    IO_Utilities.ErrorLogWriter(
                        "RC5.1H1 isolated a legacy Truck detail UI error: " +
                        ex.Message + Environment.NewLine + ex.StackTrace);
                }
            }

            PopulateModernAdvancedVehicleControls();

            AttachModernTrailerBrowseEventsH2();

            bool activeTrailerAvailable =
                !String.IsNullOrWhiteSpace(ModernActiveTrailerId) &&
                UserTrailerDictionary != null &&
                UserTrailerDictionary.ContainsKey(
                    ModernActiveTrailerId);

            if (activeTrailerAvailable)
            {
                comboBoxUserTrailerCompanyTrailers.SelectedValue =
                    ModernActiveTrailerId;
            }
            else if (comboBoxUserTrailerCompanyTrailers != null &&
                     comboBoxUserTrailerCompanyTrailers.Items.Count > 1)
            {
                comboBoxUserTrailerCompanyTrailers.SelectedIndex = 1;
            }

            UpdateModernTrailerBrowseUiH2();

            AttachModernTruckSwitchEvents();
            IO_Utilities.LogWriter(
                "RC5.1H1 load-safe truck switch UI initialized.");
            IO_Utilities.LogWriter(
                "RC5.1H2 safe tabs and Trailer browse UI initialized.");
            ApplyModernReadOnlyTabsH3();
            UpdateModernTruckSwitchUi();

            if (!ModernVehicleBaselineIsCurrent())
            {
                CaptureModernVehicleBaselines();
                UpdateModernTruckSwitchUi();
            }

            InitializeModernFleetEditingH6();
            InitializeModernFleetOperationsH7();
            InitializeModernConvoyToolsH8();
            InitializeModernVisitedCitiesH9();
            InitializeModernGaragesH10();
            InitializeModernTrailerSwitchH14();
            InitializeModernSteeringWheelH15();
            InitializeModernAccessoryPackageH19();
            InitializeModernFeatureCompletionH20();
        }
    }
}