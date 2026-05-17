using HarmonyLib;
using MGSC;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FloatingCombatText
{
    public static class Plugin
    {
        public static string ModAssemblyName => Assembly.GetExecutingAssembly().GetName().Name;
        public static Color RedColor = new Color(0.85f, 0, 0);

        [Hook(ModHookType.AfterConfigsLoaded)]
        public static void AfterConfig(IModContext context)
        {
            new Harmony("Dekar_" + ModAssemblyName).PatchAll();
        }


        [HarmonyPatch(typeof(Creature), nameof(Creature.OnWoundAdded))]
        public static class Patch_OnWoundAdded
        {
            public static void Postfix(Creature __instance, BodyPartWound bodyPartWound)
            {
                if (!__instance.IsSeenByPlayer)
                    return;

                string tag = string.Empty;

                switch (bodyPartWound.WoundCategory)
                {
                    case WoundCategory.Normal:
                        tag = $"wound.{bodyPartWound.DmgType}.{bodyPartWound.WoundSlotRecord.NatureType}.name";
                        break;
                    case WoundCategory.Amputation:
                        tag = "wound.amputation.name";
                        break;
                    case WoundCategory.Minor:
                        tag = $"wound.minor.{bodyPartWound.DmgType}.{bodyPartWound.WoundSlotRecord.NatureType}.name";
                        break;
                }
                var name = Localization.Get(tag);
                name = Regex.Replace(name, @" \(.*\)", ""); //remove damage type

                UI.Get<DungeonHudScreen>().AddFlyingDamage(__instance, 10, false, name);

                var flyingDamageHintList = typeof(DungeonHudScreen).GetField("_flyingDamage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(UI.Get<DungeonHudScreen>()) as List<FlyingDamageHint>;
                var flyingDamageHint = flyingDamageHintList.Last();
                var direction = new Vector3(0, -0.5f, 0);
                typeof(FlyingDamageHint).GetField("_rootDestPos", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(flyingDamageHint, direction, BindingFlags.SetField, null, CultureInfo.CurrentCulture);
            }
        }

        [HarmonyPatch]
        static class DamageSystemPatch
        {
            static MethodBase TargetMethod()
            {
                var predicateClass = typeof(DamageSystem).GetNestedTypes(AccessTools.all).First(n => n.Name == "<>c");
                var method = predicateClass.GetMethods(AccessTools.all).First(n => n.Name.Contains("b__13"));
                return method;
            }

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var ballisticField = AccessTools.DeclaredField(typeof(Ballistic), "InitialPosition");
                var codeMatcher2 = new CodeMatcher(instructions);
                codeMatcher2 = codeMatcher2.MatchStartForward(
                        CodeMatch.LoadsField(ballisticField)
                    );

                var codeMatcher = new CodeMatcher(instructions);

                codeMatcher = codeMatcher.MatchStartForward(
                        CodeMatch.Calls(() => default(DungeonHudScreen).AddFlyingDamage(default, default, default, default, false))
                    ).ThrowIfInvalid("AddFlyingDamage");

                var labels = codeMatcher.Instruction.ExtractLabels();

                codeMatcher = codeMatcher.RemoveInstruction()
                    .InsertAndAdvance(
                        CodeInstruction.LoadLocal(2), //Ballistic
                        new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Ballistic), "InitialPosition")),
                        CodeInstruction.Call(() => CreateFloatingText(default, default, default, default, default, default, default))
                    ).AddLabelsAt(codeMatcher.Pos - 3, labels);
                
                instructions = codeMatcher.Instructions();
                return instructions;
            }
        }

        [HarmonyPatch]
        static class CreaturePatch
        {
            static MethodBase TargetMethod()
            {
                var creatureHelperClass = typeof(Creature).GetNestedTypes(AccessTools.all).First(c => c.Name.Contains("<>c") && c.GetMethods(AccessTools.all).Any(m => m.Name.Contains("<MeleeAttack>")));
                var method = creatureHelperClass.GetMethods(AccessTools.all).First(n => n.Name.Contains("<MeleeAttack>"));
                return method;
            }

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher = codeMatcher.MatchStartForward(
                        CodeMatch.Calls(() => default(DungeonHudScreen).AddFlyingDamage(default, default, default, default, false))
                    )
                    .ThrowIfInvalid("Could not find call to AddFlyingDamage");

                var labels = codeMatcher.Instruction.ExtractLabels();

                codeMatcher = codeMatcher.RemoveInstruction()
                    .InsertAndAdvance(
                        CodeInstruction.LoadArgument(0), //ldarg.0  attacking creature
                        CodeInstruction.Call(() => CreateFloatingTextMelee(default, default, default, default, default, default, default))
                    ).AddLabelsAt(codeMatcher.Pos - 2, labels);
                
                instructions = codeMatcher.Instructions();
                return instructions;
            }
        }

        [HarmonyPatch]
        static class ExplosionPatch
        {
            static MethodBase TargetMethod()
            {
                var predicateClass = typeof(ExplosionEntity);
                var method = predicateClass.GetMethods(AccessTools.all).First(n => n.Name.Contains("ProcessExplosionCell"));
                return method;
            }

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codeMatcher = new CodeMatcher(instructions);
                codeMatcher = codeMatcher.MatchStartForward(
                        CodeMatch.Calls(() => default(DungeonHudScreen).AddFlyingDamage(default, default, default, default, false))
                    ).ThrowIfInvalid("AddFlyingDamage");

                codeMatcher = codeMatcher.RemoveInstruction()
                    .InsertAndAdvance(
                        CodeInstruction.LoadArgument(0), //this, ExplosionEntity
                        new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(ExplosionEntity), "_position")),
                        CodeInstruction.Call(() =>
                            CreateFloatingText(default, default, default, default, default, default, default))
                    );

                instructions = codeMatcher.Instructions();
                return instructions;
            }
        }

        [HarmonyPatch(typeof(FlyingDamageHint), "Initialize")]
        public static class PatchInitialize
        {
            public static void Prefix(FlyingDamageHint __instance, MapRenderer mapRenderer, Creature creature, int damage, bool isCrit, ref string wound, ref bool wasImmune, out string __state)
            {
                __state = wound;
                creature = null;
                wound = "";
                var creatureField = typeof(FlyingDamageHint).GetField("_creature", BindingFlags.Instance | BindingFlags.NonPublic);
                creatureField?.SetValue(__instance, null, BindingFlags.SetField, null, CultureInfo.CurrentCulture);
            }

            public static void Postfix(FlyingDamageHint __instance, MapRenderer mapRenderer, Creature creature, int damage, bool isCrit, string wound, ref bool wasImmune, ref TextMeshProUGUI ____damageText, ref Vector3 ____rootPos, ref Vector3 ____rootDestPos, string __state)
            {
                ____damageText.color = isCrit ? Colors.Yellow : (damage < 0 ? Colors.Green : Color.white);
                ____rootPos = creature.Creature3dView.transform.position + new Vector3(0.0f, 0.25f, 0.0f) + (Vector3)Random.insideUnitCircle*0.10f;
                ____rootDestPos = Vector3.zero;
                ____damageText.fontSize = (float)(5 + Math.Sqrt(damage));
                ____damageText.outlineWidth = 0.15f;
                ____damageText.outlineColor = Color.black;
                ____damageText.fontStyle = FontStyles.Bold;

                if (__state != string.Empty)
                {
                    //todo randomize position, speed?
                    ____damageText.text = __state;
                    ____damageText.color = RedColor;
                    ____damageText.fontSize = (float)(3 + Math.Sqrt(damage)); //todo base size on severity?
                    ____damageText.autoSizeTextContainer = true;
                }

                var canvasGroup = typeof(FlyingDamageHint).GetField("_canvasGroup", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance) as CanvasGroup;
                canvasGroup.alpha = 1;
            }
        }


        [HarmonyPatch(typeof(FlyingDamageHint), "LateUpdate")]
        public static class PatchLateUpdate
        {
            public static bool Prefix(FlyingDamageHint __instance, ref float ____time, ref float ____duration, ref Vector3 ____rootPos, ref Vector3 ____rootDestPos, ref MapRenderer ____mapRenderer, ref float ____cachedSize, ref TextMeshProUGUI ____damageText)
            {
                ____time += Time.deltaTime;
                ____rootPos += ____rootDestPos * Time.deltaTime;
                ____rootDestPos -= ____rootDestPos * 0.5f * Time.deltaTime;
                var screenPos = (Vector2)____mapRenderer.WorldToScreenPos(____rootPos);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(MGSC.UI.ScreenRoot, screenPos, null, out var localPoint);
                __instance.transform.localPosition = localPoint;
                __instance.transform.localScale = new Vector3(____cachedSize, ____cachedSize, ____cachedSize);

                if (____time < ____duration)
                    return false; //skip original code
                
                var canvasGroup = typeof(FlyingDamageHint).GetField("_canvasGroup", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(__instance) as CanvasGroup;
                canvasGroup.alpha = 0;
                return true; //run original code for cleanup
            }
        }

        [HarmonyPatch(typeof(FlyingDamageHint), "CanMerge")]
        public static class PatchCanMerge
        {
            public static bool Prefix(FlyingDamageHint __instance, ref bool __result, Creature creature, int damage, bool isCrit, string wound)
            {
                __result = false;
                return false;
            }
        }

        public static void CreateFloatingTextMelee(
            DungeonHudScreen ui,
            Creature creature,
            int damage,
            bool isCrit,
            string wound,
            bool wasImmune,
            object createHelper)
        {
            var type2 = createHelper.GetType();
            Creature attacker = AccessTools.Field(type2, "<>4__this").GetValue(createHelper) as Creature;
            var creatureData = attacker.CreatureData;
            var damageOriginCell = creatureData.Position;
            CreateFloatingText(ui, creature, damage, isCrit, wound, wasImmune, damageOriginCell);
        }

        public static void CreateFloatingText(
            DungeonHudScreen ui,
            Creature creature,
            int damage,
            bool isCrit,
            string wound,
            bool wasImmune,
            CellPosition damageOriginCell)
        {
            ui.AddFlyingDamage(creature, damage, isCrit, string.Empty);
            var flyingDamageHintList = typeof(DungeonHudScreen).GetField("_flyingDamage", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(ui) as List<FlyingDamageHint>;
            var flyingDamageHint = flyingDamageHintList.Last();
            
            Vector3 direction = (creature.CreatureData.Position - damageOriginCell).ToVector2().normalized;
            
            Vector3 perpendicular = Vector3.Cross(direction, Vector3.up);
            if (perpendicular.sqrMagnitude < 0.001f)
                perpendicular = Vector3.Cross(direction, Vector3.right);

            float angleOffset = Random.Range(-25, 25);
            float randomSpin = Random.Range(-25, 25);

            Quaternion spin = Quaternion.AngleAxis(randomSpin, direction);
            Quaternion tilt = Quaternion.AngleAxis(angleOffset, spin * perpendicular);

            direction = (tilt * direction).normalized * 0.7f * Math.Min((float)(Math.Sqrt(damage) / 5), 2);
            
            //Reuse _rootDestPos to store velocity of movement
            typeof(FlyingDamageHint).GetField("_rootDestPos", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(flyingDamageHint, direction, BindingFlags.SetField, null, CultureInfo.CurrentCulture);
        }
    }
}
