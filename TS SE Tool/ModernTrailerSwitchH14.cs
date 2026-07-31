using System;
using System.Collections.Generic;
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
        // H14 implements only the topology proven with ETS2 1.60 comparative
        // saves: the assigned player_vehicles slot and one player.my_vehicles
        // mirror slot contain the same active truck, trailer and placements.
        // The switch changes only the trailer reference in those two blocks.
        private string ModernPendingTrailerSwitchPreviousTrailerIdH14 = "";
        private string ModernPendingTrailerSwitchTargetTrailerIdH14 = "";
        private string ModernPendingTrailerSwitchActiveSlotIdH14 = "";
        private string ModernPendingTrailerSwitchMirrorSlotIdH14 = "";
        private string ModernPendingTrailerSwitchGarageIdH14 = "";
        private string ModernTrailerSwitchBaselineSavePathH14 = "";
        private DateTime ModernTrailerSwitchBaselineTimestampH14 = DateTime.MinValue;
        private bool ModernTrailerSwitchEventsAttachedH14 = false;

        private sealed class ModernTrailerSwitchPlanH14
        {
            internal string PlayerId = "";
            internal string TruckId = "";
            internal string PreviousTrailerId = "";
            internal string TargetTrailerId = "";
            internal string ActiveSlotId = "";
            internal string MirrorSlotId = "";
            internal string GarageId = "";
        }

        private bool HasPendingModernTrailerSwitchH14()
        {
            return !String.IsNullOrWhiteSpace(
                ModernPendingTrailerSwitchTargetTrailerIdH14);
        }

        private void ResetModernTrailerSwitchStateH14()
        {
            ModernPendingTrailerSwitchPreviousTrailerIdH14 = "";
            ModernPendingTrailerSwitchTargetTrailerIdH14 = "";
            ModernPendingTrailerSwitchActiveSlotIdH14 = "";
            ModernPendingTrailerSwitchMirrorSlotIdH14 = "";
            ModernPendingTrailerSwitchGarageIdH14 = "";
            ModernTrailerSwitchBaselineSavePathH14 = "";
            ModernTrailerSwitchBaselineTimestampH14 = DateTime.MinValue;
        }

        private bool ModernTrailerSwitchBaselineIsCurrentH14()
        {
            return !String.IsNullOrWhiteSpace(
                       ModernTrailerSwitchBaselineSavePathH14) &&
                   String.Equals(
                       ModernTrailerSwitchBaselineSavePathH14,
                       Globals.SelectedSavePath ?? "",
                       StringComparison.OrdinalIgnoreCase) &&
                   ModernTrailerSwitchBaselineTimestampH14 ==
                       LastModifiedTimestamp;
        }

        private bool RawModernValueEqualsH14(
            string first,
            string second)
        {
            return String.Equals(
                (first ?? "").Trim(),
                (second ?? "").Trim(),
                StringComparison.Ordinal);
        }

        private bool ModernArrayEntriesEqualH14(
            Dictionary<int, string> first,
            Dictionary<int, string> second)
        {
            if (first == null || second == null ||
                first.Count != second.Count)
            {
                return false;
            }

            foreach (KeyValuePair<int, string> item in first)
            {
                string other;
                if (!second.TryGetValue(item.Key, out other) ||
                    !RawModernValueEqualsH14(item.Value, other))
                {
                    return false;
                }
            }

            return true;
        }

        private int CountModernPlayerVehicleTrailerReferencesH14(
            string[] lines,
            string trailerId)
        {
            string normalized = NormalizeModernReference(trailerId);

            return GetModernPlayerVehicleSlots(lines)
                .Count(slot =>
                    slot.TrailerId.Equals(
                        normalized,
                        StringComparison.OrdinalIgnoreCase));
        }

        private List<string> GetModernGarageIdsForTrailerH14(
            string[] lines,
            string trailerId)
        {
            string normalized = NormalizeModernReference(trailerId);
            List<string> garages = new List<string>();

            if (lines == null || String.IsNullOrWhiteSpace(normalized))
                return garages;

            bool insideGarage = false;
            bool matchedCurrentGarage = false;
            int depth = 0;
            string currentGarageId = "";

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] ?? "";

                if (!insideGarage)
                {
                    string unitType;
                    string unitId;

                    if (!TryParseModernUnitHeader(
                            line,
                            out unitType,
                            out unitId) ||
                        !unitType.Equals(
                            "garage",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    insideGarage = true;
                    matchedCurrentGarage = false;
                    currentGarageId = unitId;
                    depth = line.Count(x => x == '{') -
                            line.Count(x => x == '}');
                    continue;
                }

                depth += line.Count(x => x == '{');
                depth -= line.Count(x => x == '}');

                if (!matchedCurrentGarage)
                {
                    string trimmed = line.Trim();
                    int colonPosition = trimmed.IndexOf(':');

                    if (colonPosition > 0)
                    {
                        string tag =
                            trimmed.Substring(0, colonPosition).Trim();

                        if (tag.StartsWith(
                                "trailers[",
                                StringComparison.Ordinal) &&
                            tag.EndsWith(
                                "]",
                                StringComparison.Ordinal) &&
                            NormalizeModernReference(
                                trimmed.Substring(
                                    colonPosition + 1).Trim())
                                .Equals(
                                    normalized,
                                    StringComparison.OrdinalIgnoreCase))
                        {
                            garages.Add(currentGarageId);
                            matchedCurrentGarage = true;
                        }
                    }
                }

                if (depth <= 0)
                {
                    insideGarage = false;
                    matchedCurrentGarage = false;
                    currentGarageId = "";
                }
            }

            return garages;
        }

        private bool IsModernTrailerAssignedToAiDriverH14(
            string[] lines,
            string trailerId)
        {
            string normalized = NormalizeModernReference(trailerId);

            if (lines == null || String.IsNullOrWhiteSpace(normalized))
                return false;

            bool insideDriver = false;
            int depth = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] ?? "";

                if (!insideDriver)
                {
                    string unitType;
                    string unitId;

                    if (!TryParseModernUnitHeader(
                            line,
                            out unitType,
                            out unitId) ||
                        !unitType.Equals(
                            "driver_ai",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    insideDriver = true;
                    depth = line.Count(x => x == '{') -
                            line.Count(x => x == '}');
                    continue;
                }

                depth += line.Count(x => x == '{');
                depth -= line.Count(x => x == '}');

                string trimmed = line.Trim();
                int colonPosition = trimmed.IndexOf(':');

                if (colonPosition > 0)
                {
                    string tag =
                        trimmed.Substring(0, colonPosition).Trim();

                    if (tag.Equals(
                            "assigned_trailer",
                            StringComparison.Ordinal) &&
                        NormalizeModernReference(
                            trimmed.Substring(
                                colonPosition + 1).Trim())
                            .Equals(
                                normalized,
                                StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                if (depth <= 0)
                    insideDriver = false;
            }

            return false;
        }

        private bool HasModernCurrentOrSelectedJobH14(
            string[] lines,
            string playerId)
        {
            foreach (string field in new string[]
                     {
                         "current_job",
                         "selected_job"
                     })
            {
                string raw;
                if (TryGetModernUnitField(
                        lines,
                        "player",
                        playerId,
                        field,
                        out raw) &&
                    !String.IsNullOrWhiteSpace(
                        NormalizeModernReference(raw)))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetModernSlotAttachmentStateH14(
            string[] lines,
            string slotId,
            out string vehiclePlacement,
            out string trailerPlacementCount,
            out Dictionary<int, string> trailerPlacements,
            out string trailerAttached,
            out string reason)
        {
            vehiclePlacement = "";
            trailerPlacementCount = "";
            trailerPlacements = new Dictionary<int, string>();
            trailerAttached = "";
            reason = "";

            if (!TryGetModernUnitField(
                    lines,
                    "player_vehicles",
                    slotId,
                    "stored_vehicle_placement",
                    out vehiclePlacement) ||
                !TryGetModernUnitField(
                    lines,
                    "player_vehicles",
                    slotId,
                    "stored_trailer_placements",
                    out trailerPlacementCount) ||
                !TryGetModernUnitField(
                    lines,
                    "player_vehicles",
                    slotId,
                    "stored_trailer_attached",
                    out trailerAttached))
            {
                reason =
                    "O slot " + slotId +
                    " não possui todos os campos modernos de posicionamento.";
                return false;
            }

            int declaredCount;
            if (!Int32.TryParse(
                    trailerPlacementCount.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out declaredCount) ||
                declaredCount != 1)
            {
                reason =
                    "A topologia H14 exige exatamente uma posição de reboque " +
                    "no slot " + slotId + ".";
                return false;
            }

            trailerPlacements = GetModernUnitArrayEntries(
                lines,
                "player_vehicles",
                slotId,
                "stored_trailer_placements");

            if (trailerPlacements.Count != 1 ||
                !trailerPlacements.ContainsKey(0))
            {
                reason =
                    "A posição do reboque está incompleta no slot " +
                    slotId + ".";
                return false;
            }

            bool attached;
            if (!Boolean.TryParse(trailerAttached.Trim(), out attached) ||
                !attached)
            {
                reason =
                    "A topologia H14 exige um reboque realmente acoplado " +
                    "nos dois slots espelhados.";
                return false;
            }

            return true;
        }

        private bool TryBuildModernTrailerSwitchPlanH14(
            string targetTrailerId,
            out ModernTrailerSwitchPlanH14 plan,
            out string reason)
        {
            plan = null;
            reason = "";

            if (!IsModernCompatibilityMode())
            {
                reason =
                    "A troca H14 é exclusiva do modo de compatibilidade moderno.";
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

            string target = NormalizeModernReference(targetTrailerId);
            if (String.IsNullOrWhiteSpace(target))
            {
                reason = "Selecione um reboque próprio válido.";
                return false;
            }

            if (String.IsNullOrWhiteSpace(ModernActiveTruckId) ||
                String.IsNullOrWhiteSpace(ModernActiveTrailerId) ||
                String.IsNullOrWhiteSpace(ModernActivePlayerVehicleSlotId))
            {
                reason =
                    "O caminhão, o reboque e o slot ativos não puderam ser " +
                    "resolvidos pelo grafo player.assigned_vehicles.";
                return false;
            }

            if (target.Equals(
                    ModernActiveTrailerId,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "Este reboque já é o reboque atual.";
                return false;
            }

            if (!String.IsNullOrWhiteSpace(
                    ModernPendingTruckSwitchTargetTruckId))
            {
                reason =
                    "Salve ou descarte a troca de caminhão pendente antes " +
                    "de preparar a troca de reboque.";
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

            if (HasModernCurrentOrSelectedJobH14(
                    tempSavefileInMemory,
                    playerId))
            {
                reason =
                    "Finalize ou cancele o trabalho ativo antes de trocar " +
                    "o reboque.";
                return false;
            }

            List<string> ownedTrailers = GetModernUnitArrayValues(
                tempSavefileInMemory,
                "player",
                playerId,
                "trailers");

            if (!ownedTrailers.Any(value => value.Equals(
                    target,
                    StringComparison.OrdinalIgnoreCase)))
            {
                reason =
                    "O reboque selecionado não está em player.trailers.";
                return false;
            }

            if (!IsOwnedModernTrailerH6(target) ||
                !IsModernTrailerObject(target) ||
                !IsModernTrailerObject(ModernActiveTrailerId))
            {
                reason =
                    "O reboque selecionado não possui um bloco trailer " +
                    "próprio e íntegro neste save.";
                return false;
            }

            UserCompanyTrailerData targetUiData;
            if (UserTrailerDictionary == null ||
                !UserTrailerDictionary.TryGetValue(target, out targetUiData) ||
                targetUiData == null ||
                !targetUiData.Main ||
                !targetUiData.Users)
            {
                reason =
                    "A H14 inicial permite somente um reboque principal, " +
                    "próprio e estacionado em garagem.";
                return false;
            }

            Trailer currentTrailer =
                (Trailer)SiiNunitData.SiiNitems[ModernActiveTrailerId];
            Trailer targetTrailer =
                (Trailer)SiiNunitData.SiiNitems[target];

            if (!String.IsNullOrWhiteSpace(
                    NormalizeModernReference(currentTrailer.slave_trailer)) ||
                !String.IsNullOrWhiteSpace(
                    NormalizeModernReference(targetTrailer.slave_trailer)))
            {
                reason =
                    "A H14 inicial não troca conjuntos com slave_trailer.";
                return false;
            }

            if (Math.Abs((float)currentTrailer.cargo_mass) > 0.001F ||
                Math.Abs((float)targetTrailer.cargo_mass) > 0.001F)
            {
                reason =
                    "A troca fica bloqueada quando o reboque atual ou o " +
                    "reboque de destino contém massa de carga.";
                return false;
            }

            if (IsModernTrailerAssignedToAiDriverH14(
                    tempSavefileInMemory,
                    target))
            {
                reason =
                    "O reboque selecionado está atribuído a um motorista de IA.";
                return false;
            }

            List<string> currentGarages =
                GetModernGarageIdsForTrailerH14(
                    tempSavefileInMemory,
                    ModernActiveTrailerId);
            List<string> targetGarages =
                GetModernGarageIdsForTrailerH14(
                    tempSavefileInMemory,
                    target);

            if (currentGarages.Count != 1 ||
                targetGarages.Count != 1 ||
                !currentGarages[0].Equals(
                    targetGarages[0],
                    StringComparison.OrdinalIgnoreCase))
            {
                reason =
                    "A H14 inicial exige que o reboque atual e o novo " +
                    "reboque pertençam à mesma garagem.";
                return false;
            }

            List<ModernPlayerVehicleSlotInfo> playerVehicleSlots =
                GetModernPlayerVehicleSlots(tempSavefileInMemory);

            if (playerVehicleSlots.Count(slot =>
                    slot.TrailerId.Equals(
                        target,
                        StringComparison.OrdinalIgnoreCase)) != 0)
            {
                reason =
                    "O reboque selecionado já está referenciado em " +
                    "player_vehicles.";
                return false;
            }

            if (playerVehicleSlots.Count(slot =>
                    slot.TrailerId.Equals(
                        ModernActiveTrailerId,
                        StringComparison.OrdinalIgnoreCase)) != 2)
            {
                reason =
                    "A topologia H14 homologável exige exatamente duas " +
                    "referências espelhadas ao reboque atual.";
                return false;
            }

            List<string> inventorySlots = GetModernUnitArrayValues(
                tempSavefileInMemory,
                "player",
                playerId,
                "my_vehicles");

            if (inventorySlots.Any(value => value.Equals(
                    ModernActivePlayerVehicleSlotId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                reason =
                    "A H14 inicial não cobre um assigned_vehicles que também " +
                    "esteja diretamente em player.my_vehicles.";
                return false;
            }

            List<ModernPlayerVehicleSlotInfo> mirrorCandidates =
                playerVehicleSlots
                    .Where(slot =>
                        inventorySlots.Any(value => value.Equals(
                            slot.SlotId,
                            StringComparison.OrdinalIgnoreCase)) &&
                        slot.VehicleId.Equals(
                            ModernActiveTruckId,
                            StringComparison.OrdinalIgnoreCase) &&
                        slot.TrailerId.Equals(
                            ModernActiveTrailerId,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (mirrorCandidates.Count != 1)
            {
                reason =
                    "Não foi encontrado exatamente um slot espelho seguro " +
                    "em player.my_vehicles.";
                return false;
            }

            string activeVehicleRaw;
            string activeTrailerRaw;
            string mirrorVehicleRaw;
            string mirrorTrailerRaw;

            if (!TryGetModernUnitField(
                    tempSavefileInMemory,
                    "player_vehicles",
                    ModernActivePlayerVehicleSlotId,
                    "vehicle",
                    out activeVehicleRaw) ||
                !TryGetModernUnitField(
                    tempSavefileInMemory,
                    "player_vehicles",
                    ModernActivePlayerVehicleSlotId,
                    "trailer",
                    out activeTrailerRaw) ||
                !TryGetModernUnitField(
                    tempSavefileInMemory,
                    "player_vehicles",
                    mirrorCandidates[0].SlotId,
                    "vehicle",
                    out mirrorVehicleRaw) ||
                !TryGetModernUnitField(
                    tempSavefileInMemory,
                    "player_vehicles",
                    mirrorCandidates[0].SlotId,
                    "trailer",
                    out mirrorTrailerRaw) ||
                !NormalizeModernReference(activeVehicleRaw).Equals(
                    ModernActiveTruckId,
                    StringComparison.OrdinalIgnoreCase) ||
                !NormalizeModernReference(mirrorVehicleRaw).Equals(
                    ModernActiveTruckId,
                    StringComparison.OrdinalIgnoreCase) ||
                !NormalizeModernReference(activeTrailerRaw).Equals(
                    ModernActiveTrailerId,
                    StringComparison.OrdinalIgnoreCase) ||
                !NormalizeModernReference(mirrorTrailerRaw).Equals(
                    ModernActiveTrailerId,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason =
                    "Os dois slots não possuem o mesmo caminhão e reboque ativos.";
                return false;
            }

            string activeVehiclePlacement;
            string activeTrailerCount;
            Dictionary<int, string> activeTrailerPlacements;
            string activeAttached;
            string mirrorVehiclePlacement;
            string mirrorTrailerCount;
            Dictionary<int, string> mirrorTrailerPlacements;
            string mirrorAttached;
            string attachmentReason;

            if (!TryGetModernSlotAttachmentStateH14(
                    tempSavefileInMemory,
                    ModernActivePlayerVehicleSlotId,
                    out activeVehiclePlacement,
                    out activeTrailerCount,
                    out activeTrailerPlacements,
                    out activeAttached,
                    out attachmentReason) ||
                !TryGetModernSlotAttachmentStateH14(
                    tempSavefileInMemory,
                    mirrorCandidates[0].SlotId,
                    out mirrorVehiclePlacement,
                    out mirrorTrailerCount,
                    out mirrorTrailerPlacements,
                    out mirrorAttached,
                    out attachmentReason))
            {
                reason = attachmentReason;
                return false;
            }

            if (!RawModernValueEqualsH14(
                    activeVehiclePlacement,
                    mirrorVehiclePlacement) ||
                !RawModernValueEqualsH14(
                    activeTrailerCount,
                    mirrorTrailerCount) ||
                !ModernArrayEntriesEqualH14(
                    activeTrailerPlacements,
                    mirrorTrailerPlacements) ||
                !RawModernValueEqualsH14(
                    activeAttached,
                    mirrorAttached))
            {
                reason =
                    "Os slots ativo e espelho não possuem posições idênticas. " +
                    "A troca foi bloqueada para evitar teletransporte incorreto.";
                return false;
            }

            plan = new ModernTrailerSwitchPlanH14();
            plan.PlayerId = playerId;
            plan.TruckId = ModernActiveTruckId;
            plan.PreviousTrailerId = ModernActiveTrailerId;
            plan.TargetTrailerId = target;
            plan.ActiveSlotId = ModernActivePlayerVehicleSlotId;
            plan.MirrorSlotId = mirrorCandidates[0].SlotId;
            plan.GarageId = currentGarages[0];
            return true;
        }

        private bool HasOtherPendingModernChangesH14(
            out string reason)
        {
            reason = "";

            if (HasPendingModernDriverSkillsH20())
            {
                reason = "Existem alterações de habilidades de motorista pendentes.";
                return true;
            }

            if (HasUnsavedModernVehicleChanges())
            {
                reason =
                    "Existem alterações de manutenção, placa, combustível, " +
                    "odômetro ou massa ainda não gravadas.";
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

        private void SetModernTrailerPendingLockH14(bool locked)
        {
            if (comboBoxUserTrailerCompanyTrailers != null)
                comboBoxUserTrailerCompanyTrailers.Enabled = !locked;

            if (buttonUserTrailerSelectCurrent != null)
                buttonUserTrailerSelectCurrent.Enabled = !locked;

            if (locked)
            {
                ApplyModernTrailerEditPermissionsH2(false, false);

                if (ModernTrailerAdvancedPanel != null)
                    ModernTrailerAdvancedPanel.Enabled = false;

                foreach (TabPage page in tabControlMain.TabPages)
                {
                    page.Enabled = page == tabPageTrailer;
                }
            }
        }

        private void UpdateModernTrailerSwitchUiH14()
        {
            if (!IsModernCompatibilityMode() ||
                buttonUserTrailerSwitchCurrent == null ||
                comboBoxUserTrailerCompanyTrailers == null)
            {
                return;
            }

            bool pending = HasPendingModernTrailerSwitchH14();
            string selected = GetSelectedModernTrailerIdH2();
            bool selectedIsActive =
                !String.IsNullOrWhiteSpace(selected) &&
                selected.Equals(
                    ModernActiveTrailerId,
                    StringComparison.OrdinalIgnoreCase);

            buttonUserTrailerSwitchCurrent.Text = pending
                ? "Troca preparada — clique em Salvar"
                : "Definir como reboque atual (H14)";

            buttonUserTrailerSwitchCurrent.Enabled =
                !pending &&
                !String.IsNullOrWhiteSpace(ModernActiveTrailerId) &&
                !String.IsNullOrWhiteSpace(selected) &&
                !selectedIsActive;

            SetModernTrailerPendingLockH14(pending);

            if (pending)
            {
                buttonUserTrailerSwitchCurrent.Enabled = false;
                toolTipMain.SetToolTip(
                    buttonUserTrailerSwitchCurrent,
                    "A troca foi preparada. Salve e recarregue o save.");
            }
            else if (selectedIsActive)
            {
                toolTipMain.SetToolTip(
                    buttonUserTrailerSwitchCurrent,
                    "Este reboque já é o reboque atual.");
            }
            else
            {
                toolTipMain.SetToolTip(
                    buttonUserTrailerSwitchCurrent,
                    "A validação estrutural completa será executada ao clicar.");
            }
        }

        private void ModernTrailerSelectionChangedH14(
            object sender,
            EventArgs e)
        {
            UpdateModernTrailerSwitchUiH14();
        }

        private void ModernTrailerSwitchCurrentH14(
            object sender,
            EventArgs e)
        {
            if (!IsModernCompatibilityMode() ||
                HasPendingModernTrailerSwitchH14())
            {
                return;
            }

            string otherReason;
            if (HasOtherPendingModernChangesH14(out otherReason))
            {
                MessageBox.Show(
                    otherReason + Environment.NewLine +
                    "Salve ou recarregue o save antes de preparar a troca.",
                    "H14 — salve as alterações primeiro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string target = GetSelectedModernTrailerIdH2();
            ModernTrailerSwitchPlanH14 plan;
            string reason;

            System.Diagnostics.Stopwatch validationWatch =
                System.Diagnostics.Stopwatch.StartNew();

            bool planReady = TryBuildModernTrailerSwitchPlanH14(
                target,
                out plan,
                out reason);

            validationWatch.Stop();

            IO_Utilities.LogWriter(
                "RC5.1H14.2 trailer switch validation: elapsed_ms=" +
                validationWatch.ElapsedMilliseconds.ToString(
                    CultureInfo.InvariantCulture) +
                ", result=" + (planReady ? "allowed" : "blocked") + ".");

            if (!planReady)
            {
                MessageBox.Show(
                    reason,
                    "H14 — troca de reboque bloqueada",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                UpdateModernTrailerSwitchUiH14();
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                "Reboque atual:" + Environment.NewLine +
                plan.PreviousTrailerId + Environment.NewLine +
                Environment.NewLine +
                "Novo reboque:" + Environment.NewLine +
                comboBoxUserTrailerCompanyTrailers.Text + Environment.NewLine +
                plan.TargetTrailerId + Environment.NewLine +
                Environment.NewLine +
                "Garagem comum: " + plan.GarageId + Environment.NewLine +
                Environment.NewLine +
                "O caminhão e as posições físicas atuais serão preservados. " +
                "Somente a referência do reboque será trocada nos dois " +
                "slots espelhados comprovados." + Environment.NewLine +
                Environment.NewLine +
                "Preparar esta troca?",
                "H14 — confirmar troca controlada",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirmation != DialogResult.Yes)
                return;

            ModernPendingTrailerSwitchPreviousTrailerIdH14 =
                plan.PreviousTrailerId;
            ModernPendingTrailerSwitchTargetTrailerIdH14 =
                plan.TargetTrailerId;
            ModernPendingTrailerSwitchActiveSlotIdH14 =
                plan.ActiveSlotId;
            ModernPendingTrailerSwitchMirrorSlotIdH14 =
                plan.MirrorSlotId;
            ModernPendingTrailerSwitchGarageIdH14 =
                plan.GarageId;
            ModernTrailerSwitchBaselineSavePathH14 =
                Globals.SelectedSavePath ?? "";
            ModernTrailerSwitchBaselineTimestampH14 =
                LastModifiedTimestamp;

            IO_Utilities.LogWriter(
                "RC5.1H14 staged trailer switch: " +
                plan.PreviousTrailerId + " -> " +
                plan.TargetTrailerId +
                ", truck=" + plan.TruckId +
                ", active_slot=" + plan.ActiveSlotId +
                ", mirror_slot=" + plan.MirrorSlotId +
                ", garage=" + plan.GarageId + ".");

            UpdateModernTrailerSwitchUiH14();

            MessageBox.Show(
                "A troca foi preparada em memória." + Environment.NewLine +
                "Clique em Salvar para aplicar." + Environment.NewLine +
                "Depois recarregue o save antes de fazer outra edição.",
                "H14 — troca preparada",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void AttachModernTrailerSwitchEventsH14()
        {
            if (ModernTrailerSwitchEventsAttachedH14)
                return;

            buttonUserTrailerSwitchCurrent.Click -=
                buttonUserTrailerSwitchCurrent_Click;
            buttonUserTrailerSwitchCurrent.Click -=
                ModernTrailerSwitchCurrentH14;
            buttonUserTrailerSwitchCurrent.Click +=
                ModernTrailerSwitchCurrentH14;

            comboBoxUserTrailerCompanyTrailers.SelectedIndexChanged -=
                ModernTrailerSelectionChangedH14;
            comboBoxUserTrailerCompanyTrailers.SelectedIndexChanged +=
                ModernTrailerSelectionChangedH14;

            ModernTrailerSwitchEventsAttachedH14 = true;
        }

        private string PatchModernTrailerSwitchDataH14(string content)
        {
            if (!HasPendingModernTrailerSwitchH14())
                return content;

            if (!ModernTrailerSwitchBaselineIsCurrentH14())
            {
                throw new InvalidOperationException(
                    "H14 trailer-switch baseline is unavailable or stale. " +
                    "Reload the save before writing.");
            }

            string[] beforeLines = content
                .Replace("\r\n", "\n")
                .Split(new char[] { '\n' });

            string playerId;
            if (!TryGetFirstModernUnitId(beforeLines, "player", out playerId))
                throw new InvalidDataException("H14 could not locate player.");

            string assignedBefore;
            if (!TryGetModernUnitField(
                    beforeLines,
                    "player",
                    playerId,
                    "assigned_vehicles",
                    out assignedBefore) ||
                !NormalizeModernReference(assignedBefore).Equals(
                    ModernPendingTrailerSwitchActiveSlotIdH14,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "H14 detected a changed assigned_vehicles pointer.");
            }

            List<string> ownedTrailersBefore = GetModernUnitArrayValues(
                beforeLines,
                "player",
                playerId,
                "trailers");
            List<string> inventorySlotsBefore = GetModernUnitArrayValues(
                beforeLines,
                "player",
                playerId,
                "my_vehicles");
            List<string> garageTrailersBefore = GetModernUnitArrayValues(
                beforeLines,
                "garage",
                ModernPendingTrailerSwitchGarageIdH14,
                "trailers");

            Dictionary<string, string> legacyBefore =
                new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string field in new string[]
                     {
                         "assigned_trailer",
                         "assigned_trailer_connected",
                         "my_trailer",
                         "my_trailer_attached",
                         "my_trailer_used"
                     })
            {
                string raw;
                if (TryGetModernUnitField(
                        beforeLines,
                        "player",
                        playerId,
                        field,
                        out raw))
                {
                    legacyBefore[field] = raw;
                }
            }

            string activeVehicleBefore;
            string activeTrailerBefore;
            string mirrorVehicleBefore;
            string mirrorTrailerBefore;

            if (!TryGetModernUnitField(
                    beforeLines,
                    "player_vehicles",
                    ModernPendingTrailerSwitchActiveSlotIdH14,
                    "vehicle",
                    out activeVehicleBefore) ||
                !TryGetModernUnitField(
                    beforeLines,
                    "player_vehicles",
                    ModernPendingTrailerSwitchActiveSlotIdH14,
                    "trailer",
                    out activeTrailerBefore) ||
                !TryGetModernUnitField(
                    beforeLines,
                    "player_vehicles",
                    ModernPendingTrailerSwitchMirrorSlotIdH14,
                    "vehicle",
                    out mirrorVehicleBefore) ||
                !TryGetModernUnitField(
                    beforeLines,
                    "player_vehicles",
                    ModernPendingTrailerSwitchMirrorSlotIdH14,
                    "trailer",
                    out mirrorTrailerBefore))
            {
                throw new InvalidDataException(
                    "H14 could not reread the two staged player_vehicles slots.");
            }

            if (!NormalizeModernReference(activeTrailerBefore).Equals(
                    ModernPendingTrailerSwitchPreviousTrailerIdH14,
                    StringComparison.OrdinalIgnoreCase) ||
                !NormalizeModernReference(mirrorTrailerBefore).Equals(
                    ModernPendingTrailerSwitchPreviousTrailerIdH14,
                    StringComparison.OrdinalIgnoreCase) ||
                !NormalizeModernReference(activeVehicleBefore).Equals(
                    NormalizeModernReference(mirrorVehicleBefore),
                    StringComparison.OrdinalIgnoreCase) ||
                CountModernPlayerVehicleTrailerReferencesH14(
                    beforeLines,
                    ModernPendingTrailerSwitchPreviousTrailerIdH14) != 2 ||
                CountModernPlayerVehicleTrailerReferencesH14(
                    beforeLines,
                    ModernPendingTrailerSwitchTargetTrailerIdH14) != 0)
            {
                throw new InvalidDataException(
                    "H14 pre-write topology no longer matches the staged plan.");
            }

            string activeVehiclePlacementBefore;
            string activeTrailerCountBefore;
            Dictionary<int, string> activePlacementsBefore;
            string activeAttachedBefore;
            string mirrorVehiclePlacementBefore;
            string mirrorTrailerCountBefore;
            Dictionary<int, string> mirrorPlacementsBefore;
            string mirrorAttachedBefore;
            string attachmentReason;

            if (!TryGetModernSlotAttachmentStateH14(
                    beforeLines,
                    ModernPendingTrailerSwitchActiveSlotIdH14,
                    out activeVehiclePlacementBefore,
                    out activeTrailerCountBefore,
                    out activePlacementsBefore,
                    out activeAttachedBefore,
                    out attachmentReason) ||
                !TryGetModernSlotAttachmentStateH14(
                    beforeLines,
                    ModernPendingTrailerSwitchMirrorSlotIdH14,
                    out mirrorVehiclePlacementBefore,
                    out mirrorTrailerCountBefore,
                    out mirrorPlacementsBefore,
                    out mirrorAttachedBefore,
                    out attachmentReason))
            {
                throw new InvalidDataException(attachmentReason);
            }

            string output = PatchModernUnitFields(
                content,
                "player_vehicles",
                ModernPendingTrailerSwitchActiveSlotIdH14,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    {
                        "trailer",
                        ModernPendingTrailerSwitchTargetTrailerIdH14
                    }
                },
                null);

            output = PatchModernUnitFields(
                output,
                "player_vehicles",
                ModernPendingTrailerSwitchMirrorSlotIdH14,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    {
                        "trailer",
                        ModernPendingTrailerSwitchTargetTrailerIdH14
                    }
                },
                null);

            string[] afterLines = output
                .Replace("\r\n", "\n")
                .Split(new char[] { '\n' });

            string assignedAfter = "";
            string activeVehicleAfter = "";
            string activeTrailerAfter = "";
            string mirrorVehicleAfter = "";
            string mirrorTrailerAfter = "";

            bool scalarValid =
                TryGetModernUnitField(
                    afterLines,
                    "player",
                    playerId,
                    "assigned_vehicles",
                    out assignedAfter) &&
                TryGetModernUnitField(
                    afterLines,
                    "player_vehicles",
                    ModernPendingTrailerSwitchActiveSlotIdH14,
                    "vehicle",
                    out activeVehicleAfter) &&
                TryGetModernUnitField(
                    afterLines,
                    "player_vehicles",
                    ModernPendingTrailerSwitchActiveSlotIdH14,
                    "trailer",
                    out activeTrailerAfter) &&
                TryGetModernUnitField(
                    afterLines,
                    "player_vehicles",
                    ModernPendingTrailerSwitchMirrorSlotIdH14,
                    "vehicle",
                    out mirrorVehicleAfter) &&
                TryGetModernUnitField(
                    afterLines,
                    "player_vehicles",
                    ModernPendingTrailerSwitchMirrorSlotIdH14,
                    "trailer",
                    out mirrorTrailerAfter);

            string activeVehiclePlacementAfter = "";
            string activeTrailerCountAfter = "";
            Dictionary<int, string> activePlacementsAfter =
                new Dictionary<int, string>();
            string activeAttachedAfter = "";
            string mirrorVehiclePlacementAfter = "";
            string mirrorTrailerCountAfter = "";
            Dictionary<int, string> mirrorPlacementsAfter =
                new Dictionary<int, string>();
            string mirrorAttachedAfter = "";

            bool attachmentValid =
                TryGetModernSlotAttachmentStateH14(
                    afterLines,
                    ModernPendingTrailerSwitchActiveSlotIdH14,
                    out activeVehiclePlacementAfter,
                    out activeTrailerCountAfter,
                    out activePlacementsAfter,
                    out activeAttachedAfter,
                    out attachmentReason) &&
                TryGetModernSlotAttachmentStateH14(
                    afterLines,
                    ModernPendingTrailerSwitchMirrorSlotIdH14,
                    out mirrorVehiclePlacementAfter,
                    out mirrorTrailerCountAfter,
                    out mirrorPlacementsAfter,
                    out mirrorAttachedAfter,
                    out attachmentReason);

            bool legacyValid = true;
            foreach (KeyValuePair<string, string> item in legacyBefore)
            {
                string rawAfter;
                if (!TryGetModernUnitField(
                        afterLines,
                        "player",
                        playerId,
                        item.Key,
                        out rawAfter) ||
                    !RawModernValueEqualsH14(item.Value, rawAfter))
                {
                    legacyValid = false;
                    break;
                }
            }

            List<string> ownedTrailersAfter = GetModernUnitArrayValues(
                afterLines,
                "player",
                playerId,
                "trailers");
            List<string> inventorySlotsAfter = GetModernUnitArrayValues(
                afterLines,
                "player",
                playerId,
                "my_vehicles");
            List<string> garageTrailersAfter = GetModernUnitArrayValues(
                afterLines,
                "garage",
                ModernPendingTrailerSwitchGarageIdH14,
                "trailers");

            if (!scalarValid ||
                !attachmentValid ||
                !legacyValid ||
                !NormalizeModernReference(assignedAfter).Equals(
                    ModernPendingTrailerSwitchActiveSlotIdH14,
                    StringComparison.OrdinalIgnoreCase) ||
                !RawModernValueEqualsH14(
                    activeVehicleBefore,
                    activeVehicleAfter) ||
                !RawModernValueEqualsH14(
                    mirrorVehicleBefore,
                    mirrorVehicleAfter) ||
                !NormalizeModernReference(activeTrailerAfter).Equals(
                    ModernPendingTrailerSwitchTargetTrailerIdH14,
                    StringComparison.OrdinalIgnoreCase) ||
                !NormalizeModernReference(mirrorTrailerAfter).Equals(
                    ModernPendingTrailerSwitchTargetTrailerIdH14,
                    StringComparison.OrdinalIgnoreCase) ||
                !RawModernValueEqualsH14(
                    activeVehiclePlacementBefore,
                    activeVehiclePlacementAfter) ||
                !RawModernValueEqualsH14(
                    mirrorVehiclePlacementBefore,
                    mirrorVehiclePlacementAfter) ||
                !RawModernValueEqualsH14(
                    activeTrailerCountBefore,
                    activeTrailerCountAfter) ||
                !RawModernValueEqualsH14(
                    mirrorTrailerCountBefore,
                    mirrorTrailerCountAfter) ||
                !ModernArrayEntriesEqualH14(
                    activePlacementsBefore,
                    activePlacementsAfter) ||
                !ModernArrayEntriesEqualH14(
                    mirrorPlacementsBefore,
                    mirrorPlacementsAfter) ||
                !RawModernValueEqualsH14(
                    activeAttachedBefore,
                    activeAttachedAfter) ||
                !RawModernValueEqualsH14(
                    mirrorAttachedBefore,
                    mirrorAttachedAfter) ||
                CountModernPlayerVehicleTrailerReferencesH14(
                    afterLines,
                    ModernPendingTrailerSwitchPreviousTrailerIdH14) != 0 ||
                CountModernPlayerVehicleTrailerReferencesH14(
                    afterLines,
                    ModernPendingTrailerSwitchTargetTrailerIdH14) != 2 ||
                !ownedTrailersBefore.SequenceEqual(
                    ownedTrailersAfter,
                    StringComparer.OrdinalIgnoreCase) ||
                !inventorySlotsBefore.SequenceEqual(
                    inventorySlotsAfter,
                    StringComparer.OrdinalIgnoreCase) ||
                !garageTrailersBefore.SequenceEqual(
                    garageTrailersAfter,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "H14 post-write validation rejected the trailer switch.");
            }

            IO_Utilities.LogWriter(
                "RC5.1H14 trailer switch validated: " +
                ModernPendingTrailerSwitchPreviousTrailerIdH14 + " -> " +
                ModernPendingTrailerSwitchTargetTrailerIdH14 +
                ", active_slot=" +
                ModernPendingTrailerSwitchActiveSlotIdH14 +
                ", mirror_slot=" +
                ModernPendingTrailerSwitchMirrorSlotIdH14 +
                ", garage=" +
                ModernPendingTrailerSwitchGarageIdH14 +
                ", placements=preserved.");

            return output;
        }

        private void InitializeModernTrailerSwitchH14()
        {
            ResetModernTrailerSwitchStateH14();
            AttachModernTrailerSwitchEventsH14();
            UpdateModernTrailerSwitchUiH14();

            IO_Utilities.LogWriter(
                "RC5.1H14 controlled mirrored-slot trailer switch initialized.");
        }
    }
}
