using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
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

[HarmonyPatch]
public class PastLifePerksPatches
{
    [HarmonyPatch(typeof(Princess), nameof(Princess.IncrementLove))]
    [HarmonyPostfix]
    public static void PastLifeLove(string charaID, int diffAmount, Result result)
    {
        try
        {
            // No result case happens during job story ends, we skip it
            // Also skip Sym, since his friendship is a completely different beast
            // Also skip friendship losses
            if (result == null || diffAmount <= 0 || charaID.IsNullOrEmptyOrWhitespace() || charaID == "sym" || result?.story?.storyID == "gamestartintro")
            {
                return;
            }

            Chara chara = Chara.FromID(charaID);
            int num = Princess.GetLove(charaID);

            // Skip if no chara is found or if card3 (achievement) is not unlocked
            if (chara == null || !chara.hasCard3)
            {
                return;
            }

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

            if (bonus <= 0)
            {
                return;
            }

            // Add the bonus to the love points
            Princess.SetLove(charaID, Princess.GetLove(charaID) + bonus, result, bonus);

            List<SkillChange> changes = result.currentSkillChanges ?? [];

            SkillChange existingChange = changes.First(c => c.chara == chara && c.value > 0);
            if (existingChange != null)
            {
                existingChange.bonusValue += bonus;
                existingChange.bonusText = existingChange.bonusText.IsNullOrEmpty() ? "Past Life" : existingChange.bonusText + ";Past Life";
            }
            else
            {
                SkillChange newChange = new SkillChange(chara, bonus);
                newChange.bonusText = "Past Life";
                changes.Add(newChange);
                Plugin.Logger.LogInfo($"{result.currentSkillChanges.Count}");
            }

            changes = Job.CrunchChanges(changes);

            // Rewrite the currentSkillChanges with the new list
            result.currentSkillChanges.Clear();
            result.currentSkillChanges.AddRange(changes);

            return;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Error in PastLifeLove: {ex}");
        }
    }

    [HarmonyPatch(typeof(Story), nameof(Story.Execute))]
    [HarmonyPostfix]
    public static void PastLifeSkills(Story __instance, Result result, bool undoing = false, bool startStoryOnly = false, bool isEnding = false)
    {
        try
        {
            string storyID = __instance.storyID;

            if (storyID != "gamestartintro" && storyID != "main_newship" && storyID != "main_newShip")
            {
                return;
            }

            List<Ending> endings = Ending.allEndings;
            List<string> skillIDs = [];

            foreach (Ending ending in endings)
            {
                if (!HasJobEnding(ending.endingID) || ending.skills == null || ending.skills.Count() == 0)
                {
                    continue;
                }

                foreach (var skill in ending.skills)
                {
                    if (skill != null && !skillIDs.Contains(skill.skillID))
                    {
                        skillIDs.Add(skill.skillID);
                    }
                }
            }

            foreach (var skillID in skillIDs)
            {
                AddSkillPoints(skillID, 5);
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Error in PastLifeSkills: {ex}");
        }
    }

    // Helper method to check if player has a specific job ending
    private static bool HasJobEnding(string imageID)
    {
        return Groundhogs.instance.seenBackgrounds.ContainsSafe("ending_" + imageID + "_f") ||
               Groundhogs.instance.seenBackgrounds.ContainsSafe("ending_" + imageID + "_m") ||
               Groundhogs.instance.seenBackgrounds.ContainsSafe("ending_" + imageID + "_nb");
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

        if (skill.isSpecial)
        {
            // Yep, no rebellion bonus, even if it sounds funny
            return;
        }

        // Add the points
        int currentValue = Princess.GetSkill(skillID, includeGear: false);
        int newValue = currentValue + value;
        Princess.SetSkill(skillID, newValue, null);

        Plugin.Logger.LogInfo($"Added {value} to {skillID}. Old value: {currentValue}, New value: {newValue}");
    }
}
