using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using TS_SE_Tool.Save.Items;
using TS_SE_Tool.Utilities;

namespace TS_SE_Tool
{
    public partial class FormMain
    {
        // H19 restores the broad accessory-list workflow of the original
        // FormVehicleEditor for modern saves, but stages only transactional
        // data_path/refund substitutions. Existing block IDs, accessory-array
        // order and every non-target field remain unchanged.
        private sealed class ModernAccessoryOptionH19
        {
            public string Path = "";
            public uint Refund = 0;
            public string Source = "";
            public int EvidenceCount = 0;
            public bool Verified = false;
            public string FriendlyName = "";

            public override string ToString()
            {
                string name = FriendlyName;
                if (String.IsNullOrWhiteSpace(name))
                    name = System.IO.Path.GetFileNameWithoutExtension(this.Path);
                if (String.IsNullOrWhiteSpace(name))
                    name = Path;

                return name + " — reembolso " +
                    Refund.ToString("N0", CultureInfo.CurrentCulture);
            }
        }

        private sealed class ModernAccessoryDescriptorH19
        {
            public string TruckId = "";
            public string TruckModelPath = "";
            public string TruckModelToken = "";
            public string AccessoryId = "";
            public string UnitType = "";
            public string RuntimeType = "";
            public string SlotKey = "";
            public string SlotDisplay = "";
            public string FriendlyName = "";
            public string CategoryDisplay = "Outros";
            public string CurrentOptionName = "";
            public string CurrentPath = "";
            public uint CurrentRefund = 0;
            public bool Editable = false;
            public string BlockReason = "";

            public override string ToString()
            {
                string part = String.IsNullOrWhiteSpace(FriendlyName)
                    ? SlotDisplay
                    : FriendlyName;
                string option = String.IsNullOrWhiteSpace(CurrentOptionName)
                    ? System.IO.Path.GetFileNameWithoutExtension(CurrentPath)
                    : CurrentOptionName;

                return part + " — " + option +
                    (Editable ? "" : " [somente leitura]");
            }
        }

        private sealed class ModernTruckChoiceH19
        {
            public string TruckId = "";
            public string ModelPath = "";
            public string ModelToken = "";
            public string Display = "";

            public override string ToString()
            {
                return Display;
            }
        }

        private sealed class ModernAccessoryChangeH19
        {
            public string TruckId = "";
            public string AccessoryId = "";
            public string UnitType = "";
            public string SlotKey = "";
            public string OriginalPath = "";
            public string TargetPath = "";
            public uint OriginalRefund = 0;
            public uint TargetRefund = 0;
            public bool TargetVerified = false;
            public string TargetSource = "";
            public string TruckDisplay = "";
            public string FriendlyName = "";
            public string OriginalOptionName = "";
            public string TargetOptionName = "";

            public override string ToString()
            {
                string truck = String.IsNullOrWhiteSpace(TruckDisplay)
                    ? TruckId
                    : TruckDisplay;
                string part = String.IsNullOrWhiteSpace(FriendlyName)
                    ? SlotKey
                    : FriendlyName;
                string original = String.IsNullOrWhiteSpace(OriginalOptionName)
                    ? System.IO.Path.GetFileNameWithoutExtension(OriginalPath)
                    : OriginalOptionName;
                string target = String.IsNullOrWhiteSpace(TargetOptionName)
                    ? System.IO.Path.GetFileNameWithoutExtension(TargetPath)
                    : TargetOptionName;

                return truck + " | " + part + ": " + original + " → " + target;
            }
        }

        private sealed class ModernAccessoryUnitSnapshotH19
        {
            public int UnitCount = 0;
            public string PathRaw = "";
            public string RefundRaw = "";
        }

        private readonly List<ModernAccessoryChangeH19>
            ModernPendingAccessoryChangesH19 =
                new List<ModernAccessoryChangeH19>();
        private string ModernAccessoryBaselineSavePathH19 = "";
        private DateTime ModernAccessoryBaselineTimestampH19 =
            DateTime.MinValue;

        private bool HasPendingModernAccessoryPackageH19()
        {
            return ModernPendingAccessoryChangesH19.Count > 0;
        }

        private void ResetModernAccessoryPackageH19()
        {
            ModernPendingAccessoryChangesH19.Clear();
            ModernAccessoryBaselineSavePathH19 = "";
            ModernAccessoryBaselineTimestampH19 = DateTime.MinValue;
        }

        private bool ModernAccessoryBaselineIsCurrentH19()
        {
            return !String.IsNullOrWhiteSpace(
                       ModernAccessoryBaselineSavePathH19) &&
                   String.Equals(
                       ModernAccessoryBaselineSavePathH19,
                       Globals.SelectedSavePath ?? "",
                       StringComparison.OrdinalIgnoreCase) &&
                   ModernAccessoryBaselineTimestampH19 ==
                       LastModifiedTimestamp;
        }

        private PropertyInfo GetModernAccessoryPropertyH19(
            object accessory,
            string propertyName)
        {
            if (accessory == null)
                return null;

            return accessory.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);
        }

        private bool TryGetModernAccessoryFieldsH19(
            object accessory,
            out string path,
            out uint refund)
        {
            path = "";
            refund = 0;

            PropertyInfo pathProperty =
                GetModernAccessoryPropertyH19(accessory, "data_path");
            PropertyInfo refundProperty =
                GetModernAccessoryPropertyH19(accessory, "refund");

            if (pathProperty == null || refundProperty == null)
                return false;

            object rawPath = pathProperty.GetValue(accessory, null);
            object rawRefund = refundProperty.GetValue(accessory, null);

            path = NormalizeModernAccessoryPathH15(
                rawPath == null ? "" : rawPath.ToString());

            if (rawRefund is uint)
                refund = (uint)rawRefund;
            else if (rawRefund != null &&
                     !UInt32.TryParse(
                         rawRefund.ToString(),
                         NumberStyles.Integer,
                         CultureInfo.InvariantCulture,
                         out refund))
                return false;

            return !String.IsNullOrWhiteSpace(path);
        }

        private string GetModernAccessoryUnitTypeH19(object accessory)
        {
            if (accessory == null)
                return "";

            switch (accessory.GetType().Name)
            {
                case "Vehicle_Accessory":
                    return "vehicle_accessory";
                case "Vehicle_Addon_Accessory":
                    return "vehicle_addon_accessory";
                case "Vehicle_Wheel_Accessory":
                    return "vehicle_wheel_accessory";
                case "Vehicle_Paint_job_Accessory":
                    return "vehicle_paint_job_accessory";
                case "Vehicle_Sound_Accessory":
                    return "vehicle_sound_accessory";
                case "Vehicle_Drv_plate_Accessory":
                    return "vehicle_drv_plate_accessory";
                case "Vehicle_Cargo_Accessory":
                    return "vehicle_cargo_accessory";
                default:
                    return "";
            }
        }

        private string GetModernTruckModelPathH19(Vehicle truck)
        {
            if (truck == null || truck.accessories == null ||
                SiiNunitData == null || SiiNunitData.SiiNitems == null)
                return "";

            foreach (string rawAccessoryId in truck.accessories)
            {
                string accessoryId = NormalizeModernReference(rawAccessoryId);
                if (String.IsNullOrWhiteSpace(accessoryId) ||
                    !SiiNunitData.SiiNitems.ContainsKey(accessoryId))
                    continue;

                string path;
                uint refund;
                if (!TryGetModernAccessoryFieldsH19(
                        SiiNunitData.SiiNitems[accessoryId],
                        out path,
                        out refund))
                    continue;

                Match match = Regex.Match(
                    path,
                    @"^/def/vehicle/truck/([^/]+)/data\.sii$",
                    RegexOptions.IgnoreCase);
                if (match.Success)
                    return path;
            }

            return "";
        }

        private string GetModernTruckModelTokenH19(string modelPath)
        {
            Match match = Regex.Match(
                NormalizeModernAccessoryPathH15(modelPath),
                @"^/def/vehicle/truck/([^/]+)/data\.sii$",
                RegexOptions.IgnoreCase);

            return match.Success
                ? match.Groups[1].Value.ToLowerInvariant()
                : "";
        }

        private string GetModernTruckFriendlyNameH19(string modelToken)
        {
            string token = (modelToken ?? "").Trim().ToLowerInvariant();
            switch (token)
            {
                case "iveco.sway": return "Iveco S-Way";
                case "iveco.hiway": return "Iveco Stralis Hi-Way";
                case "scania.s_2016": return "Scania Série S (2016)";
                case "scania.r_2016": return "Scania Série R (2016)";
                case "volvo.fh16_2012": return "Volvo FH16 (2012)";
                case "volvo.fh_2012": return "Volvo FH (2012)";
                case "mercedes.actros2014": return "Mercedes-Benz New Actros";
                case "daf.xf_euro6": return "DAF XF Euro 6";
                case "daf.xg_2021": return "DAF XG (2021)";
                case "daf.xg_plus_2021": return "DAF XG+ (2021)";
                case "renault.t": return "Renault Trucks T";
                case "man.tgx_2020": return "MAN TGX (2020)";
            }

            if (String.IsNullOrWhiteSpace(token))
                return "Modelo não identificado";

            string[] words = token.Replace('.', ' ').Replace('_', ' ')
                .Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < words.Length; index++)
            {
                if (words[index].Length <= 3)
                    words[index] = words[index].ToUpperInvariant();
                else
                    words[index] = Char.ToUpper(words[index][0], CultureInfo.CurrentCulture) +
                        words[index].Substring(1);
            }
            return String.Join(" ", words);
        }

        private string HumanizeModernAccessoryTokenH19(string token)
        {
            string raw = (token ?? "").Trim();
            string lower = raw.ToLowerInvariant();
            if (String.IsNullOrWhiteSpace(lower))
                return "Não identificado";

            switch (lower)
            {
                case "standard": return "Padrão";
                case "stock": return "Original de fábrica";
                case "normal": return "Padrão";
                case "carbon": return "Carbono";
                case "aluminium": return "Alumínio";
                case "aluminum": return "Alumínio";
                case "hiway_cab": return "Cabine Hi-Way";
                case "12_speed": return "12 marchas";
                case "16_speed": return "16 marchas";
                case "long_cover": return "Cobertura longa";
            }

            Match shape = Regex.Match(lower, @"^shape[_-]?(\d+)$");
            if (shape.Success)
                return "Modelo " + shape.Groups[1].Value;

            Match color = Regex.Match(lower, @"^color[_-]?(\d+)$");
            if (color.Success)
                return "Cor personalizada " + color.Groups[1].Value;

            Match cursor = Regex.Match(lower, @"^cursor(\d+)[_-](\d+)hp$");
            if (cursor.Success)
                return "Cursor " + cursor.Groups[1].Value + " — " +
                    cursor.Groups[2].Value + " hp";

            Match dc = Regex.Match(lower, @"^dc(\d+)[_-](\d+)(hp)?$");
            if (dc.Success)
                return "DC" + dc.Groups[1].Value + " — " +
                    dc.Groups[2].Value + " hp";

            Match axle = Regex.Match(lower, @"^(4x2|6x2|6x4|8x4)(?:[_-](\d+))?$");
            if (axle.Success)
                return axle.Groups[1].Value +
                    (axle.Groups[2].Success
                        ? " — configuração " + axle.Groups[2].Value
                        : "");

            if (Regex.IsMatch(lower, @"^\d+$"))
                return "Modelo " + lower;

            Dictionary<string, string> translations =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "front", "dianteiro" },
                    { "rear", "traseiro" },
                    { "disc", "disco" },
                    { "hub", "cubo" },
                    { "nuts", "porcas" },
                    { "tire", "pneu" },
                    { "tyre", "pneu" },
                    { "steel", "aço" },
                    { "matt", "fosco" },
                    { "matte", "fosco" },
                    { "gray", "cinza" },
                    { "grey", "cinza" },
                    { "black", "preto" },
                    { "dark", "escuro" },
                    { "paint", "pintura" },
                    { "cab", "cabine" },
                    { "cover", "cobertura" },
                    { "long", "longa" }
                };

            string[] parts = lower.Split(new char[] { '_', '-' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < parts.Length; index++)
            {
                string translated;
                if (translations.TryGetValue(parts[index], out translated))
                    parts[index] = translated;
                else if (parts[index].Length > 0)
                    parts[index] = Char.ToUpper(parts[index][0],
                        CultureInfo.CurrentCulture) + parts[index].Substring(1);
            }
            return String.Join(" ", parts);
        }

        private string GetModernAccessoryFriendlyNameH19(string slotKey)
        {
            string key = (slotKey ?? "").ToLowerInvariant();
            if (key.Contains(":accessory/steering_w")) return "Volante";
            if (key.Contains(":accessory/mirror")) return "Retrovisores";
            if (key.Contains(":accessory/s_panel")) return "Painel lateral";
            if (key.Contains(":accessory/sideskirt")) return "Saia lateral";
            if (key.Contains(":accessory/sunshld")) return "Para-sol";
            if (key.Contains(":accessory/tank")) return "Tanque de combustível";
            if (key.Contains(":accessory/trr_cables")) return "Cabos do reboque";
            if (key.EndsWith(":badge")) return "Emblema";
            if (key.EndsWith(":cabin")) return "Cabine";
            if (key.EndsWith(":chassis")) return "Chassi";
            if (key.EndsWith(":engine")) return "Motor";
            if (key.EndsWith(":head_light")) return "Faróis";
            if (key.EndsWith(":interior")) return "Interior";
            if (key.EndsWith(":paint_job")) return "Pintura";
            if (key.EndsWith(":transmission")) return "Transmissão";
            if (key == "global:f_disc") return "Disco dianteiro";
            if (key == "global:f_hub") return "Cubo dianteiro";
            if (key == "global:f_nuts") return "Porcas dianteiras";
            if (key == "global:f_tire") return "Pneu dianteiro";
            if (key == "global:r_disc") return "Disco traseiro";
            if (key == "global:r_hub") return "Cubo traseiro";
            if (key == "global:r_nuts") return "Porcas traseiras";
            if (key == "global:r_tire") return "Pneu traseiro";
            if (key.EndsWith(":data")) return "Identificação do caminhão";

            string raw = key;
            int separator = raw.LastIndexOf(':');
            if (separator >= 0 && separator + 1 < raw.Length)
                raw = raw.Substring(separator + 1);
            int slash = raw.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < raw.Length)
                raw = raw.Substring(slash + 1);
            return HumanizeModernAccessoryTokenH19(raw);
        }

        private string GetModernAccessoryCategoryH19(string slotKey)
        {
            string key = (slotKey ?? "").ToLowerInvariant();
            if (key.Contains("steering_w") || key.EndsWith(":cabin") ||
                key.EndsWith(":interior"))
                return "Cabine e interior";
            if (key.EndsWith(":engine") || key.EndsWith(":transmission") ||
                key.EndsWith(":chassis"))
                return "Desempenho e estrutura";
            if (key.StartsWith("global:f_") || key.StartsWith("global:r_"))
                return "Rodas e pneus";
            if (key.EndsWith(":head_light"))
                return "Iluminação";
            if (key.EndsWith(":paint_job"))
                return "Pintura";
            if (key.Contains(":accessory/") || key.EndsWith(":badge"))
                return "Exterior e acabamento";
            return "Outros";
        }

        private string GetModernAccessoryOptionFriendlyNameH19(
            string path,
            string slotKey)
        {
            string token = System.IO.Path.GetFileNameWithoutExtension(
                NormalizeModernAccessoryPathH15(path));
            return HumanizeModernAccessoryTokenH19(token);
        }

        private string BuildModernTruckDisplayH19(
            string truckId,
            string modelToken)
        {
            string model = GetModernTruckFriendlyNameH19(modelToken);
            return model + " — " + truckId +
                (truckId.Equals(
                    ModernActiveTruckId ?? "",
                    StringComparison.OrdinalIgnoreCase)
                    ? " — ATIVO"
                    : "");
        }

        private List<ModernTruckChoiceH19> GetModernTruckChoicesH19()
        {
            List<ModernTruckChoiceH19> result =
                new List<ModernTruckChoiceH19>();

            if (SiiNunitData == null ||
                SiiNunitData.Player == null ||
                SiiNunitData.SiiNitems == null)
                return result;

            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (string rawTruckId in
                     SiiNunitData.Player.trucks ?? new List<string>())
            {
                string truckId = NormalizeModernReference(rawTruckId);
                if (String.IsNullOrWhiteSpace(truckId) ||
                    !seen.Add(truckId) ||
                    !IsOwnedModernTruckH6(truckId) ||
                    !SiiNunitData.SiiNitems.ContainsKey(truckId))
                    continue;

                Vehicle truck = SiiNunitData.SiiNitems[truckId] as Vehicle;
                if (truck == null)
                    continue;

                string modelPath = GetModernTruckModelPathH19(truck);
                string modelToken =
                    GetModernTruckModelTokenH19(modelPath);

                result.Add(new ModernTruckChoiceH19()
                {
                    TruckId = truckId,
                    ModelPath = modelPath,
                    ModelToken = modelToken,
                    Display = BuildModernTruckDisplayH19(
                        truckId,
                        modelToken)
                });
            }

            return result
                .OrderByDescending(item =>
                    item.TruckId.Equals(
                        ModernActiveTruckId ?? "",
                        StringComparison.OrdinalIgnoreCase))
                .ThenBy(item => item.Display,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private bool TryBuildModernAccessorySlotH19(
            string path,
            string modelToken,
            out string slotKey,
            out string slotDisplay,
            out bool editable)
        {
            slotKey = "";
            slotDisplay = "";
            editable = false;

            string normalized =
                NormalizeModernAccessoryPathH15(path)
                    .ToLowerInvariant();

            if (String.IsNullOrWhiteSpace(normalized) ||
                !normalized.StartsWith("/def/vehicle/", StringComparison.Ordinal) ||
                !normalized.EndsWith(".sii", StringComparison.Ordinal) ||
                normalized.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                normalized.IndexOf('\\') >= 0 ||
                normalized.IndexOf('"') >= 0 ||
                normalized.IndexOf(':') >= 0)
                return false;

            Match truckMatch = Regex.Match(
                normalized,
                @"^/def/vehicle/truck/([^/]+)/(.+)$",
                RegexOptions.IgnoreCase);

            if (truckMatch.Success)
            {
                string pathModel = truckMatch.Groups[1].Value;
                string relative = truckMatch.Groups[2].Value;

                if (String.IsNullOrWhiteSpace(modelToken) ||
                    !pathModel.Equals(
                        modelToken,
                        StringComparison.OrdinalIgnoreCase))
                    return false;

                string[] parts = relative.Split('/');
                if (parts.Length == 1 &&
                    parts[0].Equals("data.sii", StringComparison.OrdinalIgnoreCase))
                {
                    slotKey = "truck:" + modelToken + ":data";
                    slotDisplay = "Identidade do caminhão";
                    editable = false;
                    return true;
                }

                if (parts.Length >= 3 &&
                    parts[0].Equals("accessory", StringComparison.OrdinalIgnoreCase))
                {
                    slotKey = "truck:" + modelToken + ":accessory/" +
                        parts[1];
                    slotDisplay = "Acessório / " +
                        parts[1].Replace('_', ' ');
                    editable = true;
                    return true;
                }

                if (parts.Length >= 2)
                {
                    slotKey = "truck:" + modelToken + ":" + parts[0];
                    slotDisplay = parts[0].Replace('_', ' ');
                    editable = !parts[0].Equals(
                        "data.sii",
                        StringComparison.OrdinalIgnoreCase);
                    return true;
                }

                return false;
            }

            Match globalMatch = Regex.Match(
                normalized,
                @"^/def/vehicle/([^/]+)/.+\.sii$",
                RegexOptions.IgnoreCase);

            if (globalMatch.Success)
            {
                string folder = globalMatch.Groups[1].Value;
                slotKey = "global:" + folder;
                slotDisplay = "Global / " + folder.Replace('_', ' ');
                editable = true;
                return true;
            }

            return false;
        }

        private List<ModernAccessoryDescriptorH19>
            GetModernTruckAccessoriesH19(ModernTruckChoiceH19 choice)
        {
            List<ModernAccessoryDescriptorH19> result =
                new List<ModernAccessoryDescriptorH19>();

            if (choice == null ||
                SiiNunitData == null ||
                SiiNunitData.SiiNitems == null ||
                !SiiNunitData.SiiNitems.ContainsKey(choice.TruckId))
                return result;

            Vehicle truck =
                SiiNunitData.SiiNitems[choice.TruckId] as Vehicle;
            if (truck == null || truck.accessories == null)
                return result;

            foreach (string rawAccessoryId in truck.accessories)
            {
                string accessoryId = NormalizeModernReference(rawAccessoryId);
                if (String.IsNullOrWhiteSpace(accessoryId) ||
                    !SiiNunitData.SiiNitems.ContainsKey(accessoryId))
                    continue;

                object accessory = SiiNunitData.SiiNitems[accessoryId];
                string unitType = GetModernAccessoryUnitTypeH19(accessory);
                string path;
                uint refund;

                if (String.IsNullOrWhiteSpace(unitType) ||
                    !TryGetModernAccessoryFieldsH19(
                        accessory,
                        out path,
                        out refund))
                    continue;

                string slotKey;
                string slotDisplay;
                bool editable;
                bool slotValid = TryBuildModernAccessorySlotH19(
                    path,
                    choice.ModelToken,
                    out slotKey,
                    out slotDisplay,
                    out editable);

                result.Add(new ModernAccessoryDescriptorH19()
                {
                    TruckId = choice.TruckId,
                    TruckModelPath = choice.ModelPath,
                    TruckModelToken = choice.ModelToken,
                    AccessoryId = accessoryId,
                    UnitType = unitType,
                    RuntimeType = accessory.GetType().Name,
                    SlotKey = slotValid ? slotKey : "não classificado",
                    SlotDisplay = slotValid
                        ? slotDisplay
                        : "Não classificado",
                    FriendlyName = slotValid
                        ? GetModernAccessoryFriendlyNameH19(slotKey)
                        : "Não classificado",
                    CategoryDisplay = slotValid
                        ? GetModernAccessoryCategoryH19(slotKey)
                        : "Outros",
                    CurrentOptionName = slotValid
                        ? GetModernAccessoryOptionFriendlyNameH19(path, slotKey)
                        : HumanizeModernAccessoryTokenH19(
                            System.IO.Path.GetFileNameWithoutExtension(path)),
                    CurrentPath = path,
                    CurrentRefund = refund,
                    Editable = slotValid && editable,
                    BlockReason = slotValid
                        ? (editable ? "" : "Componente estrutural protegido.")
                        : "Caminho fora das estruturas reconhecidas pelo editor original."
                });
            }

            return result
                .OrderBy(item => item.CategoryDisplay,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.FriendlyName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.CurrentPath,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Dictionary<string, List<ModernAccessoryOptionH19>>
            BuildModernAccessoryCatalogH19()
        {
            Dictionary<string, Dictionary<string, ModernAccessoryOptionH19>>
                staging =
                    new Dictionary<string, Dictionary<string, ModernAccessoryOptionH19>>(
                        StringComparer.OrdinalIgnoreCase);
            HashSet<string> conflicts = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (ModernTruckChoiceH19 truck in GetModernTruckChoicesH19())
            {
                foreach (ModernAccessoryDescriptorH19 descriptor in
                         GetModernTruckAccessoriesH19(truck))
                {
                    if (!descriptor.Editable)
                        continue;

                    string catalogKey = descriptor.UnitType + "|" +
                        descriptor.SlotKey;
                    string path = descriptor.CurrentPath;
                    string conflictKey = catalogKey + "|" + path;

                    if (!staging.ContainsKey(catalogKey))
                    {
                        staging[catalogKey] =
                            new Dictionary<string, ModernAccessoryOptionH19>(
                                StringComparer.OrdinalIgnoreCase);
                    }

                    ModernAccessoryOptionH19 existing;
                    if (staging[catalogKey].TryGetValue(path, out existing))
                    {
                        if (existing.Refund != descriptor.CurrentRefund)
                        {
                            conflicts.Add(conflictKey);
                            staging[catalogKey].Remove(path);
                        }
                        else
                        {
                            existing.EvidenceCount++;
                        }
                    }
                    else if (!conflicts.Contains(conflictKey))
                    {
                        staging[catalogKey][path] =
                            new ModernAccessoryOptionH19()
                            {
                                Path = path,
                                Refund = descriptor.CurrentRefund,
                                Source = "Detectado na frota do save",
                                EvidenceCount = 1,
                                Verified = true,
                                FriendlyName = GetModernAccessoryOptionFriendlyNameH19(
                                    path,
                                    descriptor.SlotKey)
                            };
                    }
                }
            }

            string steeringKey =
                "vehicle_addon_accessory|" +
                "truck:iveco.sway:accessory/steering_w";
            if (!staging.ContainsKey(steeringKey))
            {
                staging[steeringKey] =
                    new Dictionary<string, ModernAccessoryOptionH19>(
                        StringComparer.OrdinalIgnoreCase);
            }

            staging[steeringKey][ModernSteeringCarbonPathH15] =
                new ModernAccessoryOptionH19()
                {
                    Path = ModernSteeringCarbonPathH15,
                    Refund = ModernSteeringCarbonRefundH15,
                    Source = "Comparativo H15_VOLANTE_A/B",
                    EvidenceCount = 2,
                    Verified = true,
                    FriendlyName = "Carbono"
                };
            staging[steeringKey][ModernSteeringAluminiumPathH15] =
                new ModernAccessoryOptionH19()
                {
                    Path = ModernSteeringAluminiumPathH15,
                    Refund = ModernSteeringAluminiumRefundH15,
                    Source = "Comparativo H15_VOLANTE_A/B",
                    EvidenceCount = 2,
                    Verified = true,
                    FriendlyName = "Alumínio"
                };

            Dictionary<string, List<ModernAccessoryOptionH19>> result =
                new Dictionary<string, List<ModernAccessoryOptionH19>>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, Dictionary<string, ModernAccessoryOptionH19>>
                     item in staging)
            {
                result[item.Key] = item.Value.Values
                    .OrderBy(option => option.Path,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return result;
        }

        private List<ModernAccessoryOptionH19>
            GetModernAccessoryOptionsH19(
                ModernAccessoryDescriptorH19 descriptor,
                Dictionary<string, List<ModernAccessoryOptionH19>> catalog)
        {
            if (descriptor == null || catalog == null)
                return new List<ModernAccessoryOptionH19>();

            string key = descriptor.UnitType + "|" + descriptor.SlotKey;
            List<ModernAccessoryOptionH19> options;
            if (!catalog.TryGetValue(key, out options))
                options = new List<ModernAccessoryOptionH19>();

            if (!options.Any(option => option.Path.Equals(
                    descriptor.CurrentPath,
                    StringComparison.OrdinalIgnoreCase)))
            {
                options = new List<ModernAccessoryOptionH19>(options);
                options.Add(new ModernAccessoryOptionH19()
                {
                    Path = descriptor.CurrentPath,
                    Refund = descriptor.CurrentRefund,
                    Source = "Estado atual do caminhão",
                    EvidenceCount = 1,
                    Verified = true,
                    FriendlyName = GetModernAccessoryOptionFriendlyNameH19(
                        descriptor.CurrentPath,
                        descriptor.SlotKey)
                });
            }

            return options
                .OrderBy(option => option.FriendlyName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(option => option.Path,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<ModernAccessoryOptionH19>
            GetModernAccessoryAlternativesH19(
                ModernAccessoryDescriptorH19 descriptor,
                Dictionary<string, List<ModernAccessoryOptionH19>> catalog)
        {
            if (descriptor == null)
                return new List<ModernAccessoryOptionH19>();

            return GetModernAccessoryOptionsH19(descriptor, catalog)
                .Where(option =>
                    !option.Path.Equals(
                        descriptor.CurrentPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    option.Refund != descriptor.CurrentRefund)
                .OrderBy(option => option.FriendlyName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(option => option.Path,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool ModernAccessoryMatchesFilterH19(
            ModernAccessoryDescriptorH19 descriptor,
            Dictionary<string, List<ModernAccessoryOptionH19>> catalog,
            string filter)
        {
            if (descriptor == null)
                return false;
            if (String.IsNullOrWhiteSpace(filter))
                return true;

            if ((!String.IsNullOrWhiteSpace(descriptor.FriendlyName) &&
                 descriptor.FriendlyName.IndexOf(filter,
                     StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                (!String.IsNullOrWhiteSpace(descriptor.CurrentOptionName) &&
                 descriptor.CurrentOptionName.IndexOf(filter,
                     StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                (!String.IsNullOrWhiteSpace(descriptor.CategoryDisplay) &&
                 descriptor.CategoryDisplay.IndexOf(filter,
                     StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                (!String.IsNullOrWhiteSpace(descriptor.SlotDisplay) &&
                 descriptor.SlotDisplay.IndexOf(filter,
                     StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                (!String.IsNullOrWhiteSpace(descriptor.CurrentPath) &&
                 descriptor.CurrentPath.IndexOf(filter,
                     StringComparison.OrdinalIgnoreCase) >= 0))
                return true;

            return GetModernAccessoryAlternativesH19(descriptor, catalog)
                .Any(option =>
                    (!String.IsNullOrWhiteSpace(option.FriendlyName) &&
                     option.FriendlyName.IndexOf(filter,
                         StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                    (!String.IsNullOrWhiteSpace(option.Path) &&
                     option.Path.IndexOf(filter,
                         StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private bool IsCompatibleModernAccessoryTargetH19(
            ModernAccessoryDescriptorH19 descriptor,
            string targetPath,
            out string reason)
        {
            reason = "";

            if (descriptor == null || !descriptor.Editable)
            {
                reason = descriptor == null
                    ? "Acessório não selecionado."
                    : descriptor.BlockReason;
                return false;
            }

            string targetSlotKey;
            string targetSlotDisplay;
            bool targetEditable;
            if (!TryBuildModernAccessorySlotH19(
                    targetPath,
                    descriptor.TruckModelToken,
                    out targetSlotKey,
                    out targetSlotDisplay,
                    out targetEditable) ||
                !targetEditable)
            {
                reason = "O caminho informado não pertence a uma estrutura de acessório permitida.";
                return false;
            }

            if (!targetSlotKey.Equals(
                    descriptor.SlotKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "O caminho informado pertence a outro slot ou a outro modelo de caminhão.";
                return false;
            }

            return true;
        }

        private bool CanOpenModernAccessoryPackageH19()
        {
            return IsModernCompatibilityMode() &&
                   !HasPendingModernAccessoryPackageH19() &&
                   !HasPendingModernSteeringWheelChangeH15() &&
                   GetModernTruckChoicesH19().Count > 0;
        }

        private void SetModernAccessoryPendingLockH19(bool locked)
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

        private void UpdateModernAccessoryPackageUiH19()
        {
            if (!IsModernCompatibilityMode())
                return;

            Control editor = tabPageTruck.Controls
                .Find("buttonTruckVehicleEditor", true)
                .FirstOrDefault();
            if (editor == null)
                return;

            bool pending = HasPendingModernAccessoryPackageH19();
            editor.Enabled = !pending &&
                !HasPendingModernSteeringWheelChangeH15() &&
                GetModernTruckChoicesH19().Count > 0;

            if (pending)
            {
                toolTipMain.SetToolTip(
                    editor,
                    "H20 — pacote de peças preparado com " +
                    ModernPendingAccessoryChangesH19.Count.ToString(
                        CultureInfo.InvariantCulture) +
                    " alteração(ões). Clique em Salvar e recarregue o save.");
                SetModernAccessoryPendingLockH19(true);
            }
            else
            {
                toolTipMain.SetToolTip(
                    editor,
                    "H20 — editor de peças com nomes amigáveis e alternativas verificadas.");
            }
        }

        private void ShowModernAccessoryPackageH19()
        {
            if (!IsModernCompatibilityMode())
                return;

            if (HasPendingModernAccessoryPackageH19() ||
                HasPendingModernSteeringWheelChangeH15())
            {
                MessageBox.Show(
                    "Já existe um pacote de acessórios preparado. " +
                    "Clique em Salvar e recarregue o save.",
                    "H20 — alteração pendente",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string currentJob = NormalizeModernReference(
                SiiNunitData.Player.current_job);
            string selectedJob = NormalizeModernReference(
                SiiNunitData.Player.selected_job);

            if (!String.IsNullOrWhiteSpace(currentJob) ||
                !String.IsNullOrWhiteSpace(selectedJob))
            {
                MessageBox.Show(
                    "O pacote de acessórios exige que não exista trabalho atual nem trabalho selecionado.",
                    "H20 — trabalho ativo",
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
                    "H20 — salve as alterações primeiro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            List<ModernTruckChoiceH19> trucks = GetModernTruckChoicesH19();
            if (trucks.Count == 0)
            {
                MessageBox.Show(
                    "Nenhum caminhão próprio compatível foi encontrado.",
                    "H20 — editor indisponível",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Dictionary<string, List<ModernAccessoryOptionH19>> catalog =
                BuildModernAccessoryCatalogH19();
            List<ModernAccessoryChangeH19> staged =
                new List<ModernAccessoryChangeH19>();

            using (Form dialog = new Form())
            {
                dialog.Text = "H20 — editor de peças do caminhão (modo seguro)";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.Sizable;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = true;
                dialog.ShowInTaskbar = false;
                dialog.AutoScaleMode = AutoScaleMode.Dpi;
                dialog.ClientSize = new Size(1120, 680);
                dialog.MinimumSize = Size.Empty;

                TableLayoutPanel root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.AutoScroll = true;
                root.Padding = new Padding(12);
                root.ColumnCount = 1;
                root.RowCount = 6;
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 68F));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                dialog.Controls.Add(root);

                Label intro = new Label();
                intro.AutoSize = true;
                intro.MaximumSize = new Size(1060, 0);
                intro.Text =
                    "Escolha o caminhão, localize a peça pelo nome e selecione uma alternativa verificada. " +
                    "Peças sem alternativa aparecem como somente leitura. O editor preserva o bloco original, " +
                    "a ordem dos acessórios e todos os componentes não selecionados.";
                intro.Margin = new Padding(0, 0, 0, 8);
                root.Controls.Add(intro, 0, 0);

                TableLayoutPanel selectors = new TableLayoutPanel();
                selectors.Dock = DockStyle.Top;
                selectors.AutoSize = true;
                selectors.ColumnCount = 2;
                selectors.RowCount = 3;
                selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
                selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
                selectors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                selectors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                selectors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                selectors.Margin = new Padding(0, 0, 0, 8);
                root.Controls.Add(selectors, 0, 1);

                Label truckSelectorLabel = new Label();
                truckSelectorLabel.AutoSize = true;
                truckSelectorLabel.Text = "Caminhão:";
                truckSelectorLabel.Margin = new Padding(0, 0, 0, 3);
                selectors.Controls.Add(truckSelectorLabel, 0, 0);

                Label searchLabel = new Label();
                searchLabel.AutoSize = true;
                searchLabel.Text = "Buscar peça ou opção:";
                searchLabel.Margin = new Padding(0, 0, 0, 3);
                selectors.Controls.Add(searchLabel, 1, 0);

                ComboBox truckCombo = new ComboBox();
                truckCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                truckCombo.Dock = DockStyle.Fill;
                foreach (ModernTruckChoiceH19 truck in trucks)
                    truckCombo.Items.Add(truck);
                truckCombo.SelectedIndex = 0;
                truckCombo.Margin = new Padding(0, 0, 8, 0);
                selectors.Controls.Add(truckCombo, 0, 1);

                TextBox searchBox = new TextBox();
                searchBox.Dock = DockStyle.Fill;
                searchBox.Text = "";
                selectors.Controls.Add(searchBox, 1, 1);
                toolTipMain.SetToolTip(searchBox,
                    "Digite Volante, Chassi, Motor, Pneu, Farol, Carbono, Alumínio ou outro nome de peça/opção.");

                FlowLayoutPanel filterBar = new FlowLayoutPanel();
                filterBar.AutoSize = true;
                filterBar.Dock = DockStyle.Fill;
                filterBar.FlowDirection = FlowDirection.LeftToRight;
                filterBar.WrapContents = true;
                filterBar.Margin = new Padding(0, 6, 0, 0);

                CheckBox onlyAlternativesCheck = new CheckBox();
                onlyAlternativesCheck.AutoSize = true;
                onlyAlternativesCheck.Text = "Mostrar somente peças com alternativas";
                onlyAlternativesCheck.Margin = new Padding(0, 0, 14, 0);
                filterBar.Controls.Add(onlyAlternativesCheck);

                Label summaryLabel = new Label();
                summaryLabel.AutoSize = true;
                summaryLabel.Text = "";
                summaryLabel.Margin = new Padding(0, 3, 0, 0);
                filterBar.Controls.Add(summaryLabel);

                selectors.Controls.Add(filterBar, 0, 2);
                selectors.SetColumnSpan(filterBar, 2);

                SplitContainer split = new SplitContainer();
                split.Dock = DockStyle.Fill;
                split.Orientation = Orientation.Vertical;
                root.Controls.Add(split, 0, 2);

                dialog.Shown += delegate
                {
                    Rectangle working = Screen.FromControl(dialog).WorkingArea;
                    int targetWidth = Math.Min(
                        dialog.Width,
                        Math.Max(760, working.Width - 24));
                    int targetHeight = Math.Min(
                        dialog.Height,
                        Math.Max(500, working.Height - 24));

                    dialog.Size = new Size(targetWidth, targetHeight);
                    dialog.MinimumSize = new Size(
                        Math.Min(820, targetWidth),
                        Math.Min(520, targetHeight));
                    dialog.Location = new Point(
                        working.Left + Math.Max(0, (working.Width - dialog.Width) / 2),
                        working.Top + Math.Max(0, (working.Height - dialog.Height) / 2));

                    int available = split.ClientSize.Width - split.SplitterWidth;
                    if (available <= 0)
                        return;

                    int panel1Min = Math.Min(
                        420,
                        Math.Max(0, available / 2));
                    int panel2Min = Math.Min(
                        300,
                        Math.Max(0, available - panel1Min));
                    if (panel1Min + panel2Min > available)
                        panel2Min = Math.Max(0, available - panel1Min);

                    int desired = (int)Math.Round(available * 0.58D);
                    int maximum = Math.Max(
                        panel1Min,
                        available - panel2Min);
                    desired = Math.Max(
                        panel1Min,
                        Math.Min(desired, maximum));

                    split.Panel1MinSize = 0;
                    split.Panel2MinSize = 0;
                    split.SplitterDistance = desired;
                    split.Panel1MinSize = panel1Min;
                    split.Panel2MinSize = panel2Min;

                    IO_Utilities.LogWriter(
                        "RC5.1H20 accessory editor layout: form=" +
                        dialog.Width.ToString(CultureInfo.InvariantCulture) +
                        "x" + dialog.Height.ToString(CultureInfo.InvariantCulture) +
                        ", working=" + working.Width.ToString(
                            CultureInfo.InvariantCulture) +
                        "x" + working.Height.ToString(
                            CultureInfo.InvariantCulture) +
                        ", client_width=" + split.ClientSize.Width.ToString(
                            CultureInfo.InvariantCulture) +
                        ", splitter=" + split.SplitterDistance.ToString(
                            CultureInfo.InvariantCulture) +
                        ", panel1_min=" + split.Panel1MinSize.ToString(
                            CultureInfo.InvariantCulture) +
                        ", panel2_min=" + split.Panel2MinSize.ToString(
                            CultureInfo.InvariantCulture) + ".");
                };

                TreeView accessoryTree = new TreeView();
                accessoryTree.Dock = DockStyle.Fill;
                accessoryTree.HideSelection = false;
                accessoryTree.FullRowSelect = true;
                accessoryTree.ShowLines = true;
                accessoryTree.ShowPlusMinus = true;
                split.Panel1.Controls.Add(accessoryTree);

                TableLayoutPanel detailsLayout = new TableLayoutPanel();
                detailsLayout.Dock = DockStyle.Fill;
                detailsLayout.AutoScroll = true;
                detailsLayout.ColumnCount = 1;
                detailsLayout.RowCount = 9;
                detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                detailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                detailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                split.Panel2.Controls.Add(detailsLayout);

                Label partTitle = new Label();
                partTitle.AutoSize = true;
                partTitle.Font = new Font(dialog.Font, FontStyle.Bold);
                partTitle.Text = "Selecione uma peça";
                partTitle.Margin = new Padding(0, 0, 0, 5);
                detailsLayout.Controls.Add(partTitle, 0, 0);

                Label currentLabel = new Label();
                currentLabel.AutoSize = true;
                currentLabel.MaximumSize = new Size(470, 0);
                currentLabel.Text = "";
                currentLabel.Margin = new Padding(0, 0, 0, 8);
                detailsLayout.Controls.Add(currentLabel, 0, 1);

                Label optionLabel = new Label();
                optionLabel.AutoSize = true;
                optionLabel.Text = "Alternativas verificadas para troca:";
                optionLabel.Margin = new Padding(0, 0, 0, 4);
                detailsLayout.Controls.Add(optionLabel, 0, 2);

                ComboBox optionCombo = new ComboBox();
                optionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
                optionCombo.Dock = DockStyle.Top;
                detailsLayout.Controls.Add(optionCombo, 0, 3);

                Label optionStatus = new Label();
                optionStatus.AutoSize = true;
                optionStatus.MaximumSize = new Size(470, 0);
                optionStatus.Margin = new Padding(0, 6, 0, 8);
                detailsLayout.Controls.Add(optionStatus, 0, 4);

                Button stageButton = new Button();
                stageButton.Text = "Adicionar troca ao pacote";
                stageButton.AutoSize = true;
                stageButton.Enabled = false;
                stageButton.Anchor = AnchorStyles.Left;
                stageButton.Margin = new Padding(0, 0, 0, 10);
                detailsLayout.Controls.Add(stageButton, 0, 5);

                CheckBox technicalCheck = new CheckBox();
                technicalCheck.AutoSize = true;
                technicalCheck.Text = "Mostrar detalhes técnicos";
                technicalCheck.Margin = new Padding(0, 2, 0, 4);
                detailsLayout.Controls.Add(technicalCheck, 0, 6);

                Label technicalNotice = new Label();
                technicalNotice.AutoSize = true;
                technicalNotice.Text =
                    "Os nomes acima são amigáveis. O caminho técnico permanece disponível para auditoria.";
                technicalNotice.MaximumSize = new Size(470, 0);
                technicalNotice.Visible = false;
                detailsLayout.Controls.Add(technicalNotice, 0, 7);

                TextBox technicalDetails = new TextBox();
                technicalDetails.Multiline = true;
                technicalDetails.ReadOnly = true;
                technicalDetails.ScrollBars = ScrollBars.Both;
                technicalDetails.WordWrap = false;
                technicalDetails.Dock = DockStyle.Fill;
                technicalDetails.BackColor = SystemColors.Window;
                technicalDetails.Visible = false;
                detailsLayout.Controls.Add(technicalDetails, 0, 8);

                Action updateResponsiveWidths = delegate()
                {
                    int introWidth = Math.Max(
                        320,
                        root.ClientSize.Width - root.Padding.Horizontal - 12);
                    intro.MaximumSize = new Size(introWidth, 0);

                    int detailsWidth = Math.Max(
                        260,
                        split.Panel2.ClientSize.Width - 24);
                    currentLabel.MaximumSize = new Size(detailsWidth, 0);
                    optionStatus.MaximumSize = new Size(detailsWidth, 0);
                    technicalNotice.MaximumSize = new Size(detailsWidth, 0);
                };
                dialog.Shown += delegate
                {
                    updateResponsiveWidths();
                };
                dialog.Resize += delegate
                {
                    updateResponsiveWidths();
                };
                split.SplitterMoved += delegate
                {
                    updateResponsiveWidths();
                };

                Label pendingLabel = new Label();
                pendingLabel.AutoSize = true;
                pendingLabel.Text = "Alterações preparadas nesta sessão:";
                pendingLabel.Margin = new Padding(0, 8, 0, 4);
                root.Controls.Add(pendingLabel, 0, 3);

                ListBox pendingList = new ListBox();
                pendingList.Dock = DockStyle.Fill;
                root.Controls.Add(pendingList, 0, 4);

                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.Dock = DockStyle.Fill;
                buttons.AutoSize = true;
                buttons.WrapContents = false;
                buttons.Margin = new Padding(0, 8, 0, 0);
                root.Controls.Add(buttons, 0, 5);

                Button cancelButton = new Button();
                cancelButton.Text = "Cancelar";
                cancelButton.DialogResult = DialogResult.Cancel;
                cancelButton.AutoSize = true;
                cancelButton.Margin = new Padding(8, 0, 0, 0);
                buttons.Controls.Add(cancelButton);

                Button applyButton = new Button();
                applyButton.Text = "Aplicar pacote (0)";
                applyButton.DialogResult = DialogResult.OK;
                applyButton.AutoSize = true;
                applyButton.Enabled = false;
                buttons.Controls.Add(applyButton);

                Button removeButton = new Button();
                removeButton.Text = "Remover selecionada";
                removeButton.AutoSize = true;
                removeButton.Enabled = false;
                removeButton.Margin = new Padding(8, 0, 0, 0);
                buttons.Controls.Add(removeButton);

                List<ModernAccessoryDescriptorH19> currentDescriptors =
                    new List<ModernAccessoryDescriptorH19>();
                ModernTruckChoiceH19 currentTruck = null;

                Action refreshPending = delegate()
                {
                    pendingList.Items.Clear();
                    foreach (ModernAccessoryChangeH19 change in staged)
                        pendingList.Items.Add(change);
                    applyButton.Enabled = staged.Count > 0;
                    applyButton.Text = "Aplicar pacote (" + staged.Count.ToString(
                        CultureInfo.InvariantCulture) + ")";
                    removeButton.Enabled = pendingList.SelectedIndex >= 0;
                };

                Action refreshStageState = delegate()
                {
                    ModernAccessoryDescriptorH19 descriptor =
                        accessoryTree.SelectedNode == null
                            ? null
                            : accessoryTree.SelectedNode.Tag as
                                ModernAccessoryDescriptorH19;
                    ModernAccessoryOptionH19 option =
                        optionCombo.SelectedItem as ModernAccessoryOptionH19;
                    List<ModernAccessoryOptionH19> alternatives =
                        descriptor == null
                            ? new List<ModernAccessoryOptionH19>()
                            : GetModernAccessoryAlternativesH19(
                                descriptor,
                                catalog);

                    bool different = descriptor != null && option != null &&
                        (!option.Path.Equals(descriptor.CurrentPath,
                            StringComparison.OrdinalIgnoreCase) ||
                         option.Refund != descriptor.CurrentRefund);
                    stageButton.Enabled = descriptor != null &&
                        descriptor.Editable && option != null && different;
                    optionStatus.ForeColor = SystemColors.ControlText;

                    if (descriptor == null)
                        optionStatus.Text = "Selecione uma peça na lista.";
                    else if (!descriptor.Editable)
                    {
                        optionStatus.ForeColor = SystemColors.GrayText;
                        optionStatus.Text = "Somente leitura — " +
                            descriptor.BlockReason;
                    }
                    else if (alternatives.Count == 0)
                    {
                        optionStatus.ForeColor = SystemColors.GrayText;
                        optionStatus.Text =
                            "Somente leitura — nenhuma alternativa verificada para esta peça neste save.";
                    }
                    else if (option == null)
                        optionStatus.Text =
                            "Selecione uma alternativa verificada para habilitar o botão.";
                    else if (!different)
                        optionStatus.Text =
                            "A opção selecionada é igual ao estado atual.";
                    else
                        optionStatus.Text =
                            "Troca segura pronta para ser adicionada ao pacote.";
                };

                Action refreshDetails = delegate()
                {
                    ModernAccessoryDescriptorH19 descriptor =
                        accessoryTree.SelectedNode == null
                            ? null
                            : accessoryTree.SelectedNode.Tag as
                                ModernAccessoryDescriptorH19;

                    optionCombo.Items.Clear();
                    optionCombo.Enabled = false;
                    stageButton.Enabled = false;
                    technicalDetails.Text = "";

                    if (descriptor == null)
                    {
                        partTitle.Text = "Selecione uma peça";
                        currentLabel.Text = "";
                        optionStatus.Text =
                            "Use a busca ou abra uma categoria na lista à esquerda.";
                        return;
                    }

                    List<ModernAccessoryOptionH19> allOptions =
                        GetModernAccessoryOptionsH19(descriptor, catalog);
                    List<ModernAccessoryOptionH19> alternatives =
                        GetModernAccessoryAlternativesH19(descriptor, catalog);
                    foreach (ModernAccessoryOptionH19 option in alternatives)
                        optionCombo.Items.Add(option);

                    if (optionCombo.Items.Count > 0)
                        optionCombo.SelectedIndex = 0;

                    optionCombo.Enabled = descriptor.Editable &&
                        alternatives.Count > 0;
                    bool actionable = descriptor.Editable &&
                        alternatives.Count > 0;
                    partTitle.Text = descriptor.FriendlyName +
                        (actionable ? "" : " — somente leitura");
                    optionLabel.Text = alternatives.Count == 0
                        ? "Alternativas verificadas para troca: nenhuma"
                        : "Alternativas verificadas para troca (" +
                            alternatives.Count.ToString(
                                CultureInfo.InvariantCulture) + "):";
                    currentLabel.Text =
                        "Opção instalada: " + descriptor.CurrentOptionName +
                        Environment.NewLine +
                        "Caminhão: " +
                        (currentTruck == null
                            ? descriptor.TruckId
                            : GetModernTruckFriendlyNameH19(
                                currentTruck.ModelToken));

                    technicalDetails.Text =
                        "Caminhão: " + descriptor.TruckId + Environment.NewLine +
                        "Modelo: " + descriptor.TruckModelPath + Environment.NewLine +
                        "Tipo de bloco: " + descriptor.UnitType + Environment.NewLine +
                        "ID preservado: " + descriptor.AccessoryId + Environment.NewLine +
                        "Slot: " + descriptor.SlotKey + Environment.NewLine +
                        "Caminho atual: " + descriptor.CurrentPath + Environment.NewLine +
                        "Reembolso atual: " + descriptor.CurrentRefund.ToString(
                            CultureInfo.InvariantCulture) + Environment.NewLine +
                        "Opções verificadas no catálogo (incluindo a atual): " +
                            allOptions.Count.ToString(
                                CultureInfo.InvariantCulture) + Environment.NewLine +
                        "Alternativas seguras para troca: " +
                            alternatives.Count.ToString(
                                CultureInfo.InvariantCulture);
                    refreshStageState();
                };

                Action rebuildTree = delegate()
                {
                    string filter = (searchBox.Text ?? "").Trim();
                    int actionableCount = currentDescriptors.Count(descriptor =>
                        descriptor.Editable &&
                        GetModernAccessoryAlternativesH19(
                            descriptor,
                            catalog).Count > 0);

                    List<ModernAccessoryDescriptorH19> visibleDescriptors =
                        currentDescriptors
                            .Where(descriptor =>
                                (!onlyAlternativesCheck.Checked ||
                                 (descriptor.Editable &&
                                  GetModernAccessoryAlternativesH19(
                                      descriptor,
                                      catalog).Count > 0)) &&
                                ModernAccessoryMatchesFilterH19(
                                    descriptor,
                                    catalog,
                                    filter))
                            .ToList();

                    summaryLabel.Text =
                        currentDescriptors.Count.ToString(
                            CultureInfo.InvariantCulture) + " peça(s) | " +
                        actionableCount.ToString(
                            CultureInfo.InvariantCulture) +
                            " com alternativa(s) | " +
                        (currentDescriptors.Count - actionableCount).ToString(
                            CultureInfo.InvariantCulture) +
                            " somente leitura | " +
                        visibleDescriptors.Count.ToString(
                            CultureInfo.InvariantCulture) + " exibida(s)";

                    accessoryTree.BeginUpdate();
                    accessoryTree.Nodes.Clear();

                    foreach (IGrouping<string, ModernAccessoryDescriptorH19> group in
                             visibleDescriptors
                                .GroupBy(descriptor => descriptor.CategoryDisplay)
                                .OrderBy(group => group.Key,
                                    StringComparer.CurrentCultureIgnoreCase))
                    {
                        TreeNode category = new TreeNode(group.Key);
                        foreach (ModernAccessoryDescriptorH19 descriptor in
                                 group.OrderBy(item => item.FriendlyName,
                                    StringComparer.CurrentCultureIgnoreCase))
                        {
                            bool hasAlternative = descriptor.Editable &&
                                GetModernAccessoryAlternativesH19(
                                    descriptor,
                                    catalog).Count > 0;
                            string nodeText = descriptor.ToString();
                            if (descriptor.Editable && !hasAlternative)
                                nodeText += " [somente leitura]";

                            TreeNode node = new TreeNode(nodeText);
                            node.Tag = descriptor;
                            category.Nodes.Add(node);
                        }
                        accessoryTree.Nodes.Add(category);
                    }

                    if (visibleDescriptors.Count == 0)
                    {
                        TreeNode emptyNode = new TreeNode(
                            onlyAlternativesCheck.Checked
                                ? "Nenhuma peça com alternativa corresponde ao filtro."
                                : "Nenhuma peça corresponde ao filtro.");
                        emptyNode.ForeColor = SystemColors.GrayText;
                        accessoryTree.Nodes.Add(emptyNode);
                    }

                    accessoryTree.ExpandAll();
                    accessoryTree.EndUpdate();
                    if (accessoryTree.Nodes.Count > 0 &&
                        accessoryTree.Nodes[0].Nodes.Count > 0)
                        accessoryTree.SelectedNode =
                            accessoryTree.Nodes[0].Nodes[0];
                    else
                        refreshDetails();
                };

                Action refreshTruck = delegate()
                {
                    currentTruck =
                        truckCombo.SelectedItem as ModernTruckChoiceH19;
                    currentDescriptors = GetModernTruckAccessoriesH19(
                        currentTruck);
                    rebuildTree();

                    int actionableCount = currentDescriptors.Count(descriptor =>
                        descriptor.Editable &&
                        GetModernAccessoryAlternativesH19(
                            descriptor,
                            catalog).Count > 0);
                    IO_Utilities.LogWriter(
                        "RC5.1H20 accessory editor truck summary: truck=" +
                        (currentTruck == null ? "<none>" : currentTruck.TruckId) +
                        ", parts=" + currentDescriptors.Count.ToString(
                            CultureInfo.InvariantCulture) +
                        ", actionable=" + actionableCount.ToString(
                            CultureInfo.InvariantCulture) +
                        ", readonly=" +
                        (currentDescriptors.Count - actionableCount).ToString(
                            CultureInfo.InvariantCulture) + ".");
                };

                truckCombo.SelectedIndexChanged += delegate
                {
                    refreshTruck();
                };
                searchBox.TextChanged += delegate
                {
                    rebuildTree();
                };
                onlyAlternativesCheck.CheckedChanged += delegate
                {
                    rebuildTree();
                };
                accessoryTree.AfterSelect += delegate
                {
                    refreshDetails();
                };
                optionCombo.SelectedIndexChanged += delegate
                {
                    ModernAccessoryDescriptorH19 descriptor =
                        accessoryTree.SelectedNode == null
                            ? null
                            : accessoryTree.SelectedNode.Tag as
                                ModernAccessoryDescriptorH19;
                    ModernAccessoryOptionH19 option =
                        optionCombo.SelectedItem as ModernAccessoryOptionH19;
                    if (descriptor != null && option != null)
                    {
                        technicalDetails.Text =
                            "Caminhão: " + descriptor.TruckId + Environment.NewLine +
                            "Modelo: " + descriptor.TruckModelPath + Environment.NewLine +
                            "Tipo de bloco: " + descriptor.UnitType + Environment.NewLine +
                            "ID preservado: " + descriptor.AccessoryId + Environment.NewLine +
                            "Slot: " + descriptor.SlotKey + Environment.NewLine +
                            "Caminho atual: " + descriptor.CurrentPath + Environment.NewLine +
                            "Caminho selecionado: " + option.Path + Environment.NewLine +
                            "Reembolso: " + descriptor.CurrentRefund.ToString(
                                CultureInfo.InvariantCulture) + " → " +
                                option.Refund.ToString(CultureInfo.InvariantCulture) +
                                Environment.NewLine +
                            "Origem: " + option.Source + Environment.NewLine +
                            "Ocorrências no catálogo: " + option.EvidenceCount.ToString(
                                CultureInfo.InvariantCulture);
                    }
                    refreshStageState();
                };
                technicalCheck.CheckedChanged += delegate
                {
                    technicalNotice.Visible = technicalCheck.Checked;
                    technicalDetails.Visible = technicalCheck.Checked;
                };
                pendingList.SelectedIndexChanged += delegate
                {
                    removeButton.Enabled = pendingList.SelectedIndex >= 0;
                };
                removeButton.Click += delegate
                {
                    ModernAccessoryChangeH19 change =
                        pendingList.SelectedItem as ModernAccessoryChangeH19;
                    if (change == null)
                        return;
                    staged.Remove(change);
                    refreshPending();
                };

                stageButton.Click += delegate
                {
                    ModernAccessoryDescriptorH19 descriptor =
                        accessoryTree.SelectedNode == null
                            ? null
                            : accessoryTree.SelectedNode.Tag as
                                ModernAccessoryDescriptorH19;
                    ModernAccessoryOptionH19 option =
                        optionCombo.SelectedItem as ModernAccessoryOptionH19;
                    if (descriptor == null || option == null ||
                        !descriptor.Editable)
                        return;

                    if (!option.Verified)
                    {
                        MessageBox.Show(
                            "A opção não possui evidência suficiente para gravação.",
                            "H20 — opção não verificada",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    string reason;
                    if (!IsCompatibleModernAccessoryTargetH19(
                            descriptor,
                            option.Path,
                            out reason))
                    {
                        MessageBox.Show(
                            reason,
                            "H20 — opção rejeitada",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    if (option.Path.Equals(
                            descriptor.CurrentPath,
                            StringComparison.OrdinalIgnoreCase) &&
                        option.Refund == descriptor.CurrentRefund)
                    {
                        MessageBox.Show(
                            "A opção selecionada é igual ao estado atual.",
                            "H20 — nenhuma alteração",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    staged.RemoveAll(change => change.AccessoryId.Equals(
                        descriptor.AccessoryId,
                        StringComparison.OrdinalIgnoreCase));
                    staged.Add(new ModernAccessoryChangeH19()
                    {
                        TruckId = descriptor.TruckId,
                        AccessoryId = descriptor.AccessoryId,
                        UnitType = descriptor.UnitType,
                        SlotKey = descriptor.SlotKey,
                        OriginalPath = descriptor.CurrentPath,
                        TargetPath = option.Path,
                        OriginalRefund = descriptor.CurrentRefund,
                        TargetRefund = option.Refund,
                        TargetVerified = option.Verified,
                        TargetSource = option.Source,
                        TruckDisplay = currentTruck == null
                            ? descriptor.TruckId
                            : GetModernTruckFriendlyNameH19(
                                currentTruck.ModelToken),
                        FriendlyName = descriptor.FriendlyName,
                        OriginalOptionName = descriptor.CurrentOptionName,
                        TargetOptionName = option.FriendlyName
                    });
                    refreshPending();
                    optionStatus.Text =
                        "Troca adicionada. Você pode selecionar outras peças ou aplicar o pacote.";
                };

                refreshTruck();
                refreshPending();

                dialog.AcceptButton = applyButton;
                dialog.CancelButton = cancelButton;

                DialogResult editorResult = dialog.ShowDialog(this);
                if (editorResult != DialogResult.OK || staged.Count == 0)
                {
                    IO_Utilities.LogWriter(
                        "RC5.1H20 accessory editor closed: staged=" +
                        staged.Count.ToString(CultureInfo.InvariantCulture) +
                        ", applied=false.");
                    return;
                }
            }

            DialogResult confirmation = MessageBox.Show(
                "Alterações preparadas: " + staged.Count.ToString(
                    CultureInfo.InvariantCulture) +
                Environment.NewLine +
                "Caminhões afetados: " + staged.Select(change => change.TruckId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count().ToString(CultureInfo.InvariantCulture) +
                Environment.NewLine + Environment.NewLine +
                "A H20 preservará todos os IDs, a quantidade e a ordem das listas. " +
                "Preparar este pacote para o próximo salvamento?",
                "H20 — confirmar pacote de peças",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirmation != DialogResult.Yes)
                return;

            ModernPendingAccessoryChangesH19.Clear();
            ModernPendingAccessoryChangesH19.AddRange(staged);
            ModernAccessoryBaselineSavePathH19 =
                Globals.SelectedSavePath ?? "";
            ModernAccessoryBaselineTimestampH19 =
                LastModifiedTimestamp;

            IO_Utilities.LogWriter(
                "RC5.1H20 accessory package staged: changes=" +
                staged.Count.ToString(CultureInfo.InvariantCulture) +
                ", trucks=" + staged.Select(change => change.TruckId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count().ToString(CultureInfo.InvariantCulture) +
                ", verified_only=true.");

            foreach (ModernAccessoryChangeH19 change in staged)
            {
                IO_Utilities.LogWriter(
                    "RC5.1H20 accessory staged: truck=" + change.TruckId +
                    ", accessory=" + change.AccessoryId +
                    ", unit=" + change.UnitType +
                    ", slot=" + change.SlotKey +
                    ", path=" + change.OriginalPath + " -> " +
                    change.TargetPath +
                    ", refund=" + change.OriginalRefund.ToString(
                        CultureInfo.InvariantCulture) + " -> " +
                    change.TargetRefund.ToString(
                        CultureInfo.InvariantCulture) +
                    ", source=" + change.TargetSource + ".");
            }

            UpdateModernAccessoryPackageUiH19();

            MessageBox.Show(
                "O pacote foi preparado em memória. " +
                "Clique uma única vez em Salvar e depois recarregue o save.",
                "H20 — pacote preparado",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private List<string> GetModernTruckAccessoryIdsFromContentH19(
            string content,
            string truckId,
            out int truckUnitCount)
        {
            truckUnitCount = 0;
            List<string> result = new List<string>();
            string[] lines = content.Replace("\r\n", "\n")
                .Split(new char[] { '\n' });
            bool inside = false;
            int depth = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string unitType;
                string unitId;

                if (!inside)
                {
                    if (!TryParseModernUnitHeader(
                            lines[i], out unitType, out unitId) ||
                        !unitType.Equals("vehicle", StringComparison.Ordinal) ||
                        !unitId.Equals(
                            truckId,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    truckUnitCount++;
                    inside = true;
                    depth = lines[i].Count(ch => ch == '{') -
                        lines[i].Count(ch => ch == '}');
                    continue;
                }

                depth += lines[i].Count(ch => ch == '{');
                depth -= lines[i].Count(ch => ch == '}');
                string trimmed = (lines[i] ?? "").Trim();

                Match match = Regex.Match(
                    trimmed,
                    @"^accessories\[(\d+)\]:\s*(\S+)$",
                    RegexOptions.IgnoreCase);
                if (match.Success)
                    result.Add(NormalizeModernReference(match.Groups[2].Value));

                if (depth <= 0)
                    inside = false;
            }

            return result;
        }

        private ModernAccessoryUnitSnapshotH19
            GetModernAccessoryUnitSnapshotH19(
                string content,
                string unitType,
                string accessoryId)
        {
            ModernAccessoryUnitSnapshotH19 snapshot =
                new ModernAccessoryUnitSnapshotH19();
            string[] lines = content.Replace("\r\n", "\n")
                .Split(new char[] { '\n' });
            bool inside = false;
            int depth = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string foundType;
                string foundId;

                if (!inside)
                {
                    if (!TryParseModernUnitHeader(
                            lines[i], out foundType, out foundId) ||
                        !foundType.Equals(unitType, StringComparison.Ordinal) ||
                        !foundId.Equals(
                            accessoryId,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    snapshot.UnitCount++;
                    inside = true;
                    depth = lines[i].Count(ch => ch == '{') -
                        lines[i].Count(ch => ch == '}');
                    continue;
                }

                depth += lines[i].Count(ch => ch == '{');
                depth -= lines[i].Count(ch => ch == '}');
                string trimmed = (lines[i] ?? "").Trim();
                int colon = trimmed.IndexOf(':');
                if (colon > 0)
                {
                    string field = trimmed.Substring(0, colon).Trim();
                    string value = trimmed.Substring(colon + 1).Trim();
                    if (field.Equals(
                            "data_path",
                            StringComparison.OrdinalIgnoreCase))
                        snapshot.PathRaw = value;
                    else if (field.Equals(
                            "refund",
                            StringComparison.OrdinalIgnoreCase))
                        snapshot.RefundRaw = value;
                }

                if (depth <= 0)
                    inside = false;
            }

            return snapshot;
        }

        private string PatchModernAccessoryPackageDataH19(string content)
        {
            if (!HasPendingModernAccessoryPackageH19())
                return content;

            Stopwatch watch = Stopwatch.StartNew();

            if (!ModernAccessoryBaselineIsCurrentH19())
            {
                throw new InvalidOperationException(
                    "H19 accessory baseline is unavailable or stale. Reload the save before writing.");
            }

            if (ModernPendingAccessoryChangesH19
                .GroupBy(change => change.AccessoryId,
                    StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() != 1))
            {
                throw new InvalidDataException(
                    "H19 rejected duplicate staged changes for the same accessory block.");
            }

            Dictionary<string, List<string>> beforeLists =
                new Dictionary<string, List<string>>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string truckId in ModernPendingAccessoryChangesH19
                     .Select(change => change.TruckId)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int truckCount;
                List<string> ids =
                    GetModernTruckAccessoryIdsFromContentH19(
                        content,
                        truckId,
                        out truckCount);
                if (truckCount != 1 || ids.Count == 0)
                {
                    throw new InvalidDataException(
                        "H19 rejected the write because truck " + truckId +
                        " does not have one valid accessory list.");
                }
                beforeLists[truckId] = ids;
            }

            string output = content;

            foreach (ModernAccessoryChangeH19 change in
                     ModernPendingAccessoryChangesH19)
            {
                if (!change.TargetVerified)
                {
                    throw new InvalidDataException(
                        "H20 rejected an accessory target without verified evidence: " +
                        change.TargetPath);
                }

                List<string> truckList = beforeLists[change.TruckId];
                if (truckList.Count(id => id.Equals(
                        change.AccessoryId,
                        StringComparison.OrdinalIgnoreCase)) != 1)
                {
                    throw new InvalidDataException(
                        "H19 rejected the write because accessory " +
                        change.AccessoryId +
                        " is not referenced exactly once by truck " +
                        change.TruckId + ".");
                }

                ModernAccessoryUnitSnapshotH19 before =
                    GetModernAccessoryUnitSnapshotH19(
                        output,
                        change.UnitType,
                        change.AccessoryId);
                uint originalRefund;

                if (before.UnitCount != 1 ||
                    !TryReadModernUIntH15(
                        before.RefundRaw,
                        out originalRefund) ||
                    !NormalizeModernAccessoryPathH15(before.PathRaw).Equals(
                        change.OriginalPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    originalRefund != change.OriginalRefund)
                {
                    throw new InvalidDataException(
                        "H19 rejected the write because accessory " +
                        change.AccessoryId +
                        " changed after staging.");
                }

                string modelToken = "";
                ModernTruckChoiceH19 truck = GetModernTruckChoicesH19()
                    .FirstOrDefault(item => item.TruckId.Equals(
                        change.TruckId,
                        StringComparison.OrdinalIgnoreCase));
                if (truck != null)
                    modelToken = truck.ModelToken;

                string targetSlot;
                string targetDisplay;
                bool targetEditable;
                if (!TryBuildModernAccessorySlotH19(
                        change.TargetPath,
                        modelToken,
                        out targetSlot,
                        out targetDisplay,
                        out targetEditable) ||
                    !targetEditable ||
                    !targetSlot.Equals(
                        change.SlotKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "H19 rejected a target outside the original slot: " +
                        change.TargetPath);
                }

                output = PatchModernUnitFields(
                    output,
                    change.UnitType,
                    change.AccessoryId,
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        {
                            "data_path",
                            TextUtilities.FromStringToOutputString(
                                change.TargetPath)
                        },
                        {
                            "refund",
                            change.TargetRefund.ToString(
                                CultureInfo.InvariantCulture)
                        }
                    },
                    null);

                ModernAccessoryUnitSnapshotH19 after =
                    GetModernAccessoryUnitSnapshotH19(
                        output,
                        change.UnitType,
                        change.AccessoryId);
                uint targetRefund;
                if (after.UnitCount != 1 ||
                    !TryReadModernUIntH15(
                        after.RefundRaw,
                        out targetRefund) ||
                    !NormalizeModernAccessoryPathH15(after.PathRaw).Equals(
                        change.TargetPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    targetRefund != change.TargetRefund)
                {
                    throw new InvalidDataException(
                        "H20 post-write validation rejected accessory " +
                        change.AccessoryId + ".");
                }
            }

            foreach (KeyValuePair<string, List<string>> item in beforeLists)
            {
                int truckCount;
                List<string> afterList =
                    GetModernTruckAccessoryIdsFromContentH19(
                        output,
                        item.Key,
                        out truckCount);
                if (truckCount != 1 ||
                    !item.Value.SequenceEqual(
                        afterList,
                        StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "H20 rejected the package because the accessory list of truck " +
                        item.Key + " changed.");
                }
            }

            watch.Stop();
            IO_Utilities.LogWriter(
                "RC5.1H20 accessory package validated: changes=" +
                ModernPendingAccessoryChangesH19.Count.ToString(
                    CultureInfo.InvariantCulture) +
                ", trucks=" + beforeLists.Count.ToString(
                    CultureInfo.InvariantCulture) +
                ", verified_only=true" +
                ", elapsed_ms=" + watch.ElapsedMilliseconds.ToString(
                    CultureInfo.InvariantCulture) +
                ", accessory_lists=preserved, result=allowed.");

            return output;
        }

        private void InitializeModernAccessoryPackageH19()
        {
            ResetModernAccessoryPackageH19();
            UpdateModernAccessoryPackageUiH19();

            int truckCount = GetModernTruckChoicesH19().Count;
            Dictionary<string, List<ModernAccessoryOptionH19>> catalog =
                BuildModernAccessoryCatalogH19();
            int optionCount = catalog.Values.Sum(options => options.Count);

            IO_Utilities.LogWriter(
                "RC5.1H20 gamer-friendly accessory package initialized: trucks=" +
                truckCount.ToString(CultureInfo.InvariantCulture) +
                ", slots=" + catalog.Count.ToString(
                    CultureInfo.InvariantCulture) +
                ", verified_options=" + optionCount.ToString(
                    CultureInfo.InvariantCulture) +
                ", original_editor_types=7.");
        }
    }
}
