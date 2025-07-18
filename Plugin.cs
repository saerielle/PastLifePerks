using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Northway.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PastLifePerks;

[BepInPlugin("org.saerielle.exocolonist.pastlifeperks", MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("Exocolonist.exe")]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        var harmony = new Harmony("PastLifePerks");
        harmony.PatchAll();
        Logger.LogInfo("PastLifePerks patches done.");
    }
}

[HarmonyPatch(typeof(Princess))]
class IncrementLove_PostfixPatch
{
    [HarmonyPatch("IncrementLove", [typeof(string), typeof(int), typeof(Result)])]
    [HarmonyPostfix]
    public static void Postfix(string charaID, int diffAmount, Result result)
    {
        // No result case happens during job story ends
        if (result == null || charaID == "sym")
        {
            return;
        }

        try
        {
            if (diffAmount == 0 || diffAmount < 0)
            {
                return;
            }
            if (charaID.IsNullOrEmptyOrWhitespace())
            {
                return;
            }

            // Skip if result.story = gamestartintro
            if (result?.story?.storyID == "gamestartintro")
            {
                return;
            }

            Chara chara = Chara.FromID(charaID);
            int num = Princess.GetLove(charaID);
            if (chara != null)
            {
                if (chara.hasCard3)
                {
                    if (diffAmount > 0)
                    {
                        // Make a reputation multiplier based on current reputation with the chara
                        int currentLove = Princess.GetLove(charaID);
                        double multiplier = currentLove switch
                        {
                            < 50 => 0.5,
                            < 100 => 0.25,
                            _ => 0
                        };

                        if (multiplier == 0)
                        {
                            return;
                        }

                        if (diffAmount == 4 && multiplier != 0.5 && chara.isBirthday)
                        {
                            // diffAmount 4 means birthday present, make multiplier 0.5 for all birthday presents
                            multiplier = 0.5;
                        }

                        int bonus = (int)(diffAmount * multiplier);

                        if (bonus == 0)
                        {
                            // Roll a chance to add 1 point based on multiplier as a percentage
                            bonus = UnityEngine.Random.Range(0, 100) < (multiplier * 100) ? 1 : 0;
                        }

                        if (bonus > 0)
                        {
                            // Add the bonus to the love points
                            Princess.SetLove(charaID, Princess.GetLove(charaID) + bonus, result, bonus);

                            List<SkillChange> changes = result.currentSkillChanges;
                            List<SkillChange> changesWithUniqueCharaLove = new List<SkillChange>();
                            foreach (var change in changes)
                            {
                                // Check if the change is a love change
                                if (change.chara != null)
                                {
                                    // Find if we have existing change for this chara in the list
                                    var existingChange = changesWithUniqueCharaLove.FirstOrDefault(c => c.chara == change.chara);

                                    if (existingChange != null)
                                    {
                                        // We have existing change, add the new change to it
                                        existingChange.value += change.value;
                                    }
                                    else
                                    {
                                        // No existing change, add it to the list
                                        changesWithUniqueCharaLove.Add(change);
                                    }
                                }
                                else
                                {
                                    // No chara, add the change to the list
                                    changesWithUniqueCharaLove.Add(change);
                                }
                            }

                            if (changesWithUniqueCharaLove.Count == changes.Count)
                            {
                                // All changes are unique, we can just return
                                return;
                            }
                            // Rewrite the currentSkillChanges with the new list
                            result.currentSkillChanges.Clear();
                            result.currentSkillChanges.AddRange(changesWithUniqueCharaLove);

                            return;
                        }
                        else
                        {
                            // No bonus, return
                            return;
                        }

                    }
                    else
                    {
                        // No bonus, return
                        return;
                    }
                }
                else
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Error in IncrementLove_Postfix: {ex}");
        }
    }
}


// Listen in on events in Story.Execute and log them
[HarmonyPatch(typeof(Story))]
class Story_ExecutePatch
{
    [HarmonyPatch("Execute")]
    [HarmonyPostfix]
    public static void Postfix(Story __instance, Result result, bool undoing = false, bool startStoryOnly = false, bool isEnding = false)
    {
        try
        {
            if (__instance.storyID == "gamestartintro" || __instance.storyID == "main_newship" || __instance.storyID == "main_newShip")
            {
                Plugin.Logger.LogInfo($"Story is {__instance.storyID}, applying stat bonuses if player has job endings.");
                // Apply the bonuses
                if (HasJobEnding("ending_doctor") || HasJobEnding("ending_parent")) AddSkillPoints("empathy", 5);
                if (HasJobEnding("ending_lawyer") || HasJobEnding("ending_governor")) AddSkillPoints("persuasion", 5);
                if (HasJobEnding("ending_novelist")) AddSkillPoints("creativity", 5);
                if (HasJobEnding("ending_entertainer") || HasJobEnding("ending_astronaut")) AddSkillPoints("bravery", 5);
                if (HasJobEnding("ending_professor")) AddSkillPoints("reasoning", 5);
                if (HasJobEnding("ending_collector") || HasJobEnding("ending_merchant")) AddSkillPoints("organization", 5);
                if (HasJobEnding("ending_roboticist")) AddSkillPoints("engineering", 5);
                if (HasJobEnding("ending_botanist") || HasJobEnding("ending_farmer")) AddSkillPoints("biology", 5);
                if (HasJobEnding("ending_architect") || HasJobEnding("ending_athlete")) AddSkillPoints("toughness", 5);
                if (HasJobEnding("ending_explorer")) AddSkillPoints("perception", 5);
                if (HasJobEnding("ending_hero") || HasJobEnding("ending_hunter")) AddSkillPoints("combat", 5);
                if (HasJobEnding("ending_rancher")) AddSkillPoints("animals", 5);

                return;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Error in Story_ExecutePostfix: {ex}");
        }
    }

    // Helper method to check if player has a specific job ending
    private static bool HasJobEnding(string imageID)
    {
        return Groundhogs.instance.seenBackgrounds.ContainsSafe(imageID + "_f") ||
               Groundhogs.instance.seenBackgrounds.ContainsSafe(imageID + "_m") ||
               Groundhogs.instance.seenBackgrounds.ContainsSafe(imageID + "_nb");
    }

    // Helper method to add skill points
    private static void AddSkillPoints(string skillID, int value)
    {
        // Get the skill instance
        Skill skill = Skill.FromID(skillID);
        if (skill == null)
        {
            Plugin.Logger.LogError($"Could not find skill with ID: {skillID}");
            return;
        }

        // Add the points
        int currentValue = Princess.GetSkill(skillID, includeGear: false);
        int newValue = currentValue + value;
        Princess.SetSkill(skillID, newValue, null);

        Plugin.Logger.LogInfo($"Added {value} to {skillID}. Old value: {currentValue}, New value: {newValue}");
    }
}
