using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
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

            // ComboBox draws the entries by calling ToString(), and a nested class has
            // no way back to the resource manager. The line is therefore rendered once,
            // where the translation helper is reachable, and just handed out here.
            public string Display;

            public override string ToString()
            {
                return Display ?? Id;
            }
        }

        private ToolStripMenuItem ModernDriverHireMenuH23;
        private ToolStripMenuItem ModernDriverFireMenuH23;
        private bool ModernDriverButtonsWiredH23;

        // RC5.1H23 user-visible text.
        //
        // Everything the user reads goes through the program's own
        // PlainTXTResourceManager, so the Language menu really switches it. The
        // English literal stays in the code as a fallback, for two reasons: the
        // lang folder is shipped next to the exe and may be missing, and
        // PlainTXTResourceManager only keeps a key that Default\lngfile.txt
        // already has. A missing resource must not leave a blank dialog.
        //
        // Values in the language files may carry a literal \r\n; that is the
        // project convention and is turned into a real line break below.
        private string ModernDriverTextH23(string key, string fallback)
        {
            try
            {
                PlainTXTResourceManager manager = ResourceManagerMain;

                if (manager != null)
                {
                    string translated = manager.GetString(key, Thread.CurrentThread.CurrentUICulture);

                    if (!String.IsNullOrEmpty(translated))
                        return translated.Replace(@"\r\n", Environment.NewLine);
                }
            }
            catch
            { }

            return fallback;
        }

        // Same, for the strings that carry a driver id, a city or a garage id.
        private string ModernDriverFormattedH23(string key, string fallbackFormat, params object[] arguments)
        {
            string template = ModernDriverTextH23(key, fallbackFormat);

            try
            {
                return String.Format(CultureInfo.InvariantCulture, template, arguments);
            }
            catch
            {
                return String.Format(CultureInfo.InvariantCulture, fallbackFormat, arguments);
            }
        }

        private string ModernDriverCaptionH23
        {
            get { return ModernDriverTextH23("modernDriverHireH23Caption", "RC5.1H23"); }
        }

        private string BuildModernDriverChoiceLineH23(ModernDriverChoiceH23 choice)
        {
            string experienceLabel = ModernDriverTextH23("modernDriverHireH23LabelExperience", "XP");

            return choice.Id + "  " + choice.Where + "  " + experienceLabel + " " +
                choice.Experience.ToString(CultureInfo.InvariantCulture) + "  " + choice.Skills;
        }

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
                reason = ModernDriverTextH23("modernDriverHireH23ReasonNotModern",
                    "Only available for modern saves (format above 74).");
                return false;
            }

            if (tempSavefileInMemory == null || tempSavefileInMemory.Length == 0)
            {
                reason = ModernDriverTextH23("modernDriverHireH23ReasonNoSave", "No modern save loaded.");
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

                    ModernDriverChoiceH23 choice = new ModernDriverChoiceH23
                    {
                        Id = id,
                        Where = ModernDriverTextH23("modernDriverHireH23WhereRecruitment", "recruitment"),
                        Experience = experience,
                        Skills = parts.Length > 1 ? parts[1] : ""
                    };

                    choice.Display = BuildModernDriverChoiceLineH23(choice);
                    result.Add(choice);
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

                ModernDriverChoiceH23 choice = new ModernDriverChoiceH23
                {
                    Id = pair.Key,
                    Where = pair.Value,
                    Experience = experience,
                    Skills = parts.Length > 1 ? parts[1] : ""
                };

                choice.Display = BuildModernDriverChoiceLineH23(choice);
                result.Add(choice);
            }

            result.Sort(delegate(ModernDriverChoiceH23 a, ModernDriverChoiceH23 b)
            {
                return String.CompareOrdinal(a.Id, b.Id);
            });

            return result;
        }

        private static string FindModernGarageOfDriverH23(List<string> lines, string driverId)
        {
            for (int index = 0; index < lines.Count; index++)
            {
                if (!lines[index].StartsWith("garage : ", StringComparison.Ordinal))
                    continue;

                int end = GetModernBlockEndH23(lines, index);
                if (end < 0)
                    continue;

                string garageId = lines[index].Substring("garage : ".Length).TrimEnd(' ', '{').Trim();

                for (int line = index + 1; line < end; line++)
                {
                    if (!lines[line].StartsWith(" drivers[", StringComparison.Ordinal))
                        continue;

                    int colon = lines[line].IndexOf(": ", StringComparison.Ordinal);
                    if (colon < 0)
                        continue;

                    if (lines[line].Substring(colon + 2).Trim() == driverId)
                        return garageId;
                }
            }

            return "";
        }

        // Full consistency check across the three ownership containers and the garage
        // slots. Anything reported here means the game would reject the save or mangle
        // it silently, so the caller must refuse to write it.
        private static bool ValidateModernDriverStateH23(List<string> lines, out string problem)
        {
            problem = "";

            Dictionary<string, string> parked = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int index = 0; index < lines.Count; index++)
            {
                if (!lines[index].StartsWith("garage : ", StringComparison.Ordinal))
                    continue;

                int end = GetModernBlockEndH23(lines, index);
                if (end < 0)
                {
                    problem = "garage block is not terminated: " + lines[index];
                    return false;
                }

                string garageId = lines[index].Substring("garage : ".Length).TrimEnd(' ', '{').Trim();
                int slots = 0;

                for (int line = index + 1; line < end; line++)
                {
                    if (!lines[line].StartsWith(" drivers[", StringComparison.Ordinal))
                        continue;

                    slots++;

                    int colon = lines[line].IndexOf(": ", StringComparison.Ordinal);
                    if (colon < 0)
                    {
                        problem = "malformed driver slot in " + garageId;
                        return false;
                    }

                    string id = lines[line].Substring(colon + 2).Trim();
                    if (id == "null")
                        continue;

                    string previous;
                    if (parked.TryGetValue(id, out previous))
                    {
                        problem = "driver " + id + " occupies two slots (" + previous + " and " + garageId + ")";
                        return false;
                    }

                    parked[id] = garageId;
                }

                string capacityText = GetModernFieldH23(lines, index + 1, end, "status");
                int status;
                int capacity = 0;
                if (capacityText != null &&
                    Int32.TryParse(capacityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out status))
                    capacity = GetModernGarageCapacityH23(status);

                if (capacity > 0 && slots != capacity)
                {
                    problem = "garage " + garageId + " has " + slots +
                        " driver slots but capacity " + capacity;
                    return false;
                }
            }

            HashSet<string> roster = new HashSet<string>(StringComparer.Ordinal);

            int countIdx;
            List<int> itemIdx;
            int declared;

            if (GetModernArrayCountH23(lines, "player", "drivers", out countIdx, out itemIdx, out declared) >= 0)
            {
                foreach (int at in itemIdx)
                {
                    int colon = lines[at].IndexOf(": ", StringComparison.Ordinal);
                    if (colon >= 0)
                        roster.Add(lines[at].Substring(colon + 2).Trim());
                }
            }

            foreach (KeyValuePair<string, string> pair in parked)
            {
                if (roster.Contains(pair.Key))
                    continue;

                problem = "driver " + pair.Key + " is parked in " + pair.Value +
                    " but missing from the company roster";
                return false;
            }

            foreach (string id in roster)
            {
                if (IndexOfModernArrayItemH23(lines, "economy", "drivers_offer", id) >= 0)
                {
                    problem = "driver " + id + " is both in the roster and in the recruitment list";
                    return false;
                }
            }

            return true;
        }

        // Older builds of this module put a driver into a garage without registering
        // him in the company roster. Such a save still loads, but the driver is not a
        // real employee and the two management screens disagree about the head count.
        // Register them before validating so the write stays consistent.
        private static void RepairMissingRosterEntriesH23(List<string> lines)
        {
            HashSet<string> roster = new HashSet<string>(StringComparer.Ordinal);

            int countIdx;
            List<int> itemIdx;
            int declared;

            if (GetModernArrayCountH23(lines, "player", "drivers", out countIdx, out itemIdx, out declared) >= 0)
            {
                foreach (int at in itemIdx)
                {
                    int colon = lines[at].IndexOf(": ", StringComparison.Ordinal);
                    if (colon >= 0)
                        roster.Add(lines[at].Substring(colon + 2).Trim());
                }
            }

            List<string> missing = new List<string>();

            for (int index = 0; index < lines.Count; index++)
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

                    string id = lines[line].Substring(colon + 2).Trim();
                    if (!id.StartsWith("driver.", StringComparison.Ordinal))
                        continue;
                    if (roster.Contains(id))
                        continue;

                    roster.Add(id);
                    missing.Add(id);
                }
            }

            foreach (string id in missing)
            {
                AppendModernArrayItemH23(lines, "player", "drivers", id);
                AppendModernArrayItemH23(lines, "player", "driver_flags", "0");
                AppendModernArrayItemH23(lines, "player", "driver_readiness_timer", "0");

                IO_Utilities.LogWriter("RC5.1H23 roster repair: registered " + id +
                    " which was parked in a garage without being on the company roster");
            }
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
                message = ModernDriverFormattedH23("modernDriverHireH23ErrGarageUnavailable",
                    "Garage has no free slot, or does not exist: {0}", garageId);
                return false;
            }

            if (!GetModernHireCandidatesH23().Any(x => x.Id == driverId))
            {
                message = ModernDriverFormattedH23("modernDriverHireH23ErrDriverNotHireable",
                    "Driver not available for hiring: {0}", driverId);
                return false;
            }

            List<string> lines = new List<string>(tempSavefileInMemory);

            // 1) occupy a free garage slot
            int garageHeader = FindModernUnitHeaderH23(lines, "garage : " + garageId);
            if (garageHeader < 0)
            {
                message = ModernDriverFormattedH23("modernDriverHireH23ErrGarageBlockMissing",
                    "Garage block not found: {0}", garageId);
                return false;
            }

            int garageEnd = GetModernBlockEndH23(lines, garageHeader);
            if (garageEnd < 0)
            {
                message = ModernDriverFormattedH23("modernDriverHireH23ErrGarageBlockIncomplete",
                    "Garage block incomplete: {0}", garageId);
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
                message = ModernDriverFormattedH23("modernDriverHireH23ErrGarageNoFreeSlot",
                    "Garage has no free driver slot: {0}", garageId);
                return false;
            }

            int slotColon = lines[slotLine].IndexOf(": ", StringComparison.Ordinal);
            lines[slotLine] = lines[slotLine].Substring(0, slotColon + 2) + driverId;

            // 2) driver block
            int driverHeader = FindModernUnitHeaderH23(lines, "driver_ai : " + driverId);
            if (driverHeader < 0)
            {
                message = ModernDriverFormattedH23("modernDriverHireH23ErrDriverBlockMissing",
                    "Driver block not found: {0}", driverId);
                return false;
            }

            int driverEnd = GetModernBlockEndH23(lines, driverHeader);
            if (driverEnd < 0)
            {
                message = ModernDriverFormattedH23("modernDriverHireH23ErrDriverBlockIncomplete",
                    "Driver block incomplete: {0}", driverId);
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
                message = ModernDriverTextH23("modernDriverHireH23ErrRosterRegister",
                    "Could not register the driver in the company roster (player block).");
                return false;
            }

            // 4) pull him out of the recruitment list - do this last,
            //    it is the only step that removes a line
            int offerIndex = IndexOfModernArrayItemH23(lines, "economy", "drivers_offer", driverId);
            if (offerIndex >= 0)
            {
                if (!RemoveModernArrayItemAtH23(lines, "economy", "drivers_offer", offerIndex))
                {
                    message = ModernDriverTextH23("modernDriverHireH23ErrOfferRemove",
                        "Could not remove the driver from the recruitment list.");
                    return false;
                }
            }

            RepairMissingRosterEntriesH23(lines);

            string inconsistency;
            if (!ValidateModernDriverStateH23(lines, out inconsistency))
            {
                message = "Refused, the save would be left inconsistent: " + inconsistency;
                return false;
            }

            tempSavefileInMemory = lines.ToArray();

            message = ModernDriverFormattedH23("modernDriverHireH23OkHired",
                "Hired {0} in {1}." + Environment.NewLine + "Written when you save the game.",
                driverId, garage.City);

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
                message = ModernDriverFormattedH23("modernDriverHireH23ErrDriverNotEmployed",
                    "Driver is not employed: {0}", driverId);
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
                message = ModernDriverFormattedH23("modernDriverHireH23ErrDriverNoGarageSlot",
                    "The driver does not occupy a slot in any garage: {0}", driverId);
                return false;
            }

            // 2) mark the driver as dismissed
            int driverHeader = FindModernUnitHeaderH23(lines, "driver_ai : " + driverId);
            if (driverHeader < 0)
            {
                message = ModernDriverFormattedH23("modernDriverHireH23ErrDriverBlockMissing",
                    "Driver block not found: {0}", driverId);
                return false;
            }

            int driverEnd = GetModernBlockEndH23(lines, driverHeader);
            if (driverEnd < 0)
            {
                message = ModernDriverFormattedH23("modernDriverHireH23ErrDriverBlockIncomplete",
                    "Driver block incomplete: {0}", driverId);
                return false;
            }

            string stateText = GetModernFieldH23(lines, driverHeader + 1, driverEnd, "state");
            uint state;
            if (stateText == null ||
                !UInt32.TryParse(stateText, NumberStyles.Integer, CultureInfo.InvariantCulture, out state))
            {
                message = ModernDriverFormattedH23("modernDriverHireH23ErrDriverStateMissing",
                    "Driver state field not found: {0}", driverId);
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
                    message = ModernDriverTextH23("modernDriverHireH23ErrRosterRemove",
                        "Could not remove the driver from the company roster.");
                    return false;
                }

                RemoveModernArrayItemAtH23(lines, "player", "driver_flags", rosterIndex);
                RemoveModernArrayItemAtH23(lines, "player", "driver_readiness_timer", rosterIndex);
            }

            // 4) remember the dismissal
            AppendModernArrayItemH23(lines, "player", "dismissed_drivers", driverId);

            RepairMissingRosterEntriesH23(lines);

            string inconsistency;
            if (!ValidateModernDriverStateH23(lines, out inconsistency))
            {
                message = "Refused, the save would be left inconsistent: " + inconsistency;
                return false;
            }

            tempSavefileInMemory = lines.ToArray();

            message = ModernDriverFormattedH23("modernDriverHireH23OkFired",
                "Fired {0}." + Environment.NewLine + "Written when you save the game.", driverId);

            IO_Utilities.LogWriter("RC5.1H23 driver fired: " + driverId);

            return true;
        }

        private void EnsureModernDriverMenusH23()
        {
            if (ModernDriverHireMenuH23 == null)
            {
                ModernDriverHireMenuH23 = new ToolStripMenuItem();
                ModernDriverHireMenuH23.Name = "toolStripMenuItemDriverHireH23";
                // Looked up by the control name on purpose: ChangeLanguage() re-translates
                // menu drop-down items by Name, so this entry follows a live language
                // switch instead of waiting for a restart.
                ModernDriverHireMenuH23.Text = ModernDriverTextH23(ModernDriverHireMenuH23.Name,
                    "Hire AI driver (RC5.1H23)");
                ModernDriverHireMenuH23.ToolTipText = ModernDriverTextH23("modernDriverHireH23MenuHireTip",
                    "Occupies a free garage slot and registers the driver in the company.");
                ModernDriverHireMenuH23.Click += delegate { ShowModernDriverDialogH23(true); };
            }

            if (ModernDriverFireMenuH23 == null)
            {
                ModernDriverFireMenuH23 = new ToolStripMenuItem();
                ModernDriverFireMenuH23.Name = "toolStripMenuItemDriverFireH23";
                ModernDriverFireMenuH23.Text = ModernDriverTextH23(ModernDriverFireMenuH23.Name,
                    "Fire AI driver (RC5.1H23)");
                ModernDriverFireMenuH23.ToolTipText = ModernDriverTextH23("modernDriverHireH23MenuFireTip",
                    "Releases the garage slot and marks the driver as dismissed.");
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
                MessageBox.Show(reason, ModernDriverCaptionH23, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<ModernHireGarageH23> garages = hire ? GetModernHireGaragesH23() : new List<ModernHireGarageH23>();
            List<ModernDriverChoiceH23> drivers = hire ? GetModernHireCandidatesH23() : GetModernFireCandidatesH23();

            if (hire && garages.Count == 0)
            {
                MessageBox.Show(
                    ModernDriverTextH23("modernDriverHireH23MsgNoGarageFree",
                        "No garage has a free driver slot." + Environment.NewLine +
                        "Buy or upgrade a garage in game before hiring."),
                    ModernDriverCaptionH23, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (drivers.Count == 0)
            {
                MessageBox.Show(
                    hire ? ModernDriverTextH23("modernDriverHireH23MsgNoCandidateHire",
                               "No driver available in the recruitment list of this save.")
                         : ModernDriverTextH23("modernDriverHireH23MsgNoCandidateFire",
                               "No employed driver occupies a garage slot."),
                    ModernDriverCaptionH23, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (Form dialog = new Form())
            {
                dialog.Text = hire
                    ? ModernDriverTextH23("modernDriverHireH23TitleHire", "RC5.1H23 - hire AI driver")
                    : ModernDriverTextH23("modernDriverHireH23TitleFire", "RC5.1H23 - fire AI driver");
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ClientSize = new System.Drawing.Size(560, 210);

                int top = 16;

                if (hire)
                {
                    Label garageLabel = new Label();
                    garageLabel.Text = ModernDriverTextH23("modernDriverHireH23LabelGarage", "Garage:");
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
                driverLabel.Text = hire
                    ? ModernDriverTextH23("modernDriverHireH23LabelDriverHire", "Driver in the recruitment list:")
                    : ModernDriverTextH23("modernDriverHireH23LabelDriverFire", "Employed driver:");
                driverLabel.Location = new System.Drawing.Point(16, top);
                driverLabel.AutoSize = true;

                ComboBox driverBox = new ComboBox();
                driverBox.Name = "comboBoxModernDriverChoiceH23";
                driverBox.DropDownStyle = ComboBoxStyle.DropDownList;
                driverBox.Location = new System.Drawing.Point(16, top + 22);
                driverBox.Size = new System.Drawing.Size(528, 24);
                driverBox.DataSource = drivers;

                Label noteLabel = new Label();
                noteLabel.Text = ModernDriverTextH23("modernDriverHireH23LabelNote",
                    "No cost - the save is edited directly.");
                noteLabel.Location = new System.Drawing.Point(16, top + 56);
                noteLabel.AutoSize = true;

                Button okButton = new Button();
                okButton.Text = hire
                    ? ModernDriverTextH23("modernDriverHireH23ButtonHire", "Hire")
                    : ModernDriverTextH23("modernDriverHireH23ButtonFire", "Fire");
                okButton.Location = new System.Drawing.Point(354, 160);
                okButton.Size = new System.Drawing.Size(90, 30);
                okButton.DialogResult = DialogResult.OK;

                Button cancelButton = new Button();
                cancelButton.Text = ModernDriverTextH23("modernDriverHireH23ButtonCancel", "Cancel");
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
                        MessageBox.Show(message, ModernDriverCaptionH23, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    if (MessageBox.Show(
                            ModernDriverFormattedH23("modernDriverHireH23ConfirmFire",
                                "Fire {0} ?" + Environment.NewLine + Environment.NewLine +
                                "The garage slot becomes free and the driver leaves the company roster.",
                                selectedDriver.Id),
                            ModernDriverCaptionH23, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;

                    if (!FireModernDriverH23(selectedDriver.Id, out message))
                    {
                        MessageBox.Show(message, ModernDriverCaptionH23, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                MessageBox.Show(message, ModernDriverCaptionH23, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void InitializeModernDriverHireH23()
        {
            EnsureModernDriverMenusH23();

            if (!IsModernCompatibilityMode())
                return;

            EnableModernDriverButtonsH23();
        }

        // H20 deliberately disabled the Hire/Fire controls of the company tab because
        // the contract was never proven. Now that both operations are implemented for
        // modern saves, the same two controls drive them instead of opening a separate
        // dialog from the menu.
        private void EnableModernDriverButtonsH23()
        {
            if (!ModernDriverButtonsWiredH23)
            {
                buttonUserCompanyDriversHire.Click -= buttonUserCompanyDriversHire_Click;
                buttonUserCompanyDriversHire.Click += delegate { ShowModernDriverDialogH23(true); };

                buttonUserCompanyDriversFire.Click -= buttonUserCompanyDriversFire_Click;
                buttonUserCompanyDriversFire.Click += delegate { ShowModernDriverDialogH23(false); };

                contextMenuStripCompanyDriversHire.Click -= contextMenuStripCompanyDriversHire_Click;
                contextMenuStripCompanyDriversHire.Click += delegate { ShowModernDriverDialogH23(true); };

                contextMenuStripCompanyDriversFire.Click -= contextMenuStripCompanyDriversFire_Click;
                contextMenuStripCompanyDriversFire.Click += delegate { ShowModernDriverDialogH23(false); };

                // H20 keeps disabling these controls from its own selection /
                // mouse handlers. Those handlers were subscribed first, so ours
                // run afterwards and put the controls back into an enabled state.
                listBoxUserCompanyDrivers.SelectedIndexChanged += ModernDriverKeepControlsEnabledH23;
                listBoxUserCompanyDrivers.MouseDown += ModernDriverKeepControlsEnabledMouseH23;

                ModernDriverButtonsWiredH23 = true;
            }

            ApplyModernDriverControlsStateH23();
        }

        private void ModernDriverKeepControlsEnabledH23(object sender, EventArgs e)
        {
            ApplyModernDriverControlsStateH23();
        }

        private void ModernDriverKeepControlsEnabledMouseH23(object sender, MouseEventArgs e)
        {
            ApplyModernDriverControlsStateH23();
        }

        private void ApplyModernDriverControlsStateH23()
        {
            if (!IsModernCompatibilityMode())
                return;

            buttonUserCompanyDriversHire.Enabled = true;
            buttonUserCompanyDriversFire.Enabled = true;
            contextMenuStripCompanyDriversHire.Enabled = true;
            contextMenuStripCompanyDriversFire.Enabled = true;

            contextMenuStripCompanyDriversHire.ToolTipText =
                ModernDriverTextH23("modernDriverHireH23ContextHireTip",
                    "RC5.1H23: hire a driver from the recruitment list.");
            contextMenuStripCompanyDriversFire.ToolTipText =
                ModernDriverTextH23("modernDriverHireH23ContextFireTip",
                    "RC5.1H23: fire an employed driver.");

            toolTipMain.SetToolTip(buttonUserCompanyDriversHire,
                ModernDriverTextH23("modernDriverHireH23ButtonHireTip",
                    "RC5.1H23: hire a driver from the recruitment list into a garage with a free slot."));
            toolTipMain.SetToolTip(buttonUserCompanyDriversFire,
                ModernDriverTextH23("modernDriverHireH23ButtonFireTip",
                    "RC5.1H23: fire an employed driver, freeing his garage slot."));
        }
    }
}
