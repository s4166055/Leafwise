using UnityEditor;
using UnityEngine;
using com.IvanMurzak.Unity.MCP;

namespace Forage.EditorTools
{
    /// <summary>
    /// Exports every registered Unity-MCP tool (core + ProBuilder + Navigation
    /// + Animation + Terrain extensions) as SKILL.md files into the user's
    /// GLOBAL Claude skills folder, and points the plugin there permanently.
    /// </summary>
    public static class SkillExporter
    {
        const string GlobalSkillsPath = @"C:\Users\JAY\.claude\skills";

        /// <summary>
        /// The plugin's cloud reconnect loop (unauthorized) spams the console and
        /// can deadlock domain reloads. Keep it off; re-enable from its window if
        /// the team ever logs in to ai-game.dev.
        /// </summary>
        [MenuItem("Forage/Disable MCP Plugin Auto-Connect")]
        public static void DisableMcpAutoConnect()
        {
            UnityMcpPluginEditor.KeepConnected = false;
            UnityMcpPluginEditor.Instance.Save();
            Debug.Log("[Forage] Unity-MCP plugin KeepConnected = false.");
        }

        [MenuItem("Forage/Export MCP Skills (Global)")]
        public static void ExportGlobal()
        {
            System.IO.Directory.CreateDirectory(GlobalSkillsPath);

            var mcpPlugin = UnityMcpPluginEditor.Instance.McpPluginInstance;
            if (mcpPlugin == null)
            {
                Debug.LogError("[Forage] McpPluginInstance not initialized yet — try again shortly.");
                return;
            }

            UnityMcpPluginEditor.SkillsPath = GlobalSkillsPath; // persist: future regens go global too
            mcpPlugin.GenerateSkillFiles(UnityMcpPluginEditor.ProjectRootPath);

            int count = System.IO.Directory.GetDirectories(GlobalSkillsPath).Length;
            Debug.Log($"[Forage] Exported MCP skills globally to {GlobalSkillsPath} ({count} skill folders).");
        }
    }
}
