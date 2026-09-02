using UnityEngine;

namespace Navigation.Tools
{
    /// <summary>
    /// ★ 編輯器工具 ★
    /// 依專案實際使用的算繪管線挑選可用的 Shader。
    ///
    /// 為什麼需要它：原本的程式直接寫死 Shader.Find("Universal Render Pipeline/Lit")。
    /// 那在 URP 專案沒問題，但這包功能是要放進別人的專案的——如果對方用的是內建算繪管線
    /// （Built-in Render Pipeline），Shader.Find 會回傳 null，Unity 不會報編譯錯誤，
    /// 而是把材質畫成洋紅色。車子跟導航線整個變粉紅色、卻沒有任何錯誤訊息可查，
    /// 是最難追的那種問題，所以這裡改成「找不到 URP 就退回內建管線的 Standard」。
    /// </summary>
    public static class NavigationShaderCompat
    {
        /// <summary>會受光照影響的不透明表面用 Shader（車身、建物量體等）。</summary>
        public static Shader Lit()
        {
            return Find("Universal Render Pipeline/Lit", "Standard", "Diffuse");
        }

        /// <summary>不受光照影響的純色 Shader（導航線、標線等）。</summary>
        public static Shader Unlit()
        {
            return Find("Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default");
        }

        private static Shader Find(params string[] candidates)
        {
            foreach (string name in candidates)
            {
                Shader shader = Shader.Find(name);
                if (shader != null)
                {
                    return shader;
                }
            }

            // 全部落空時回傳 Unity 內建的錯誤 Shader，至少畫面上看得出來是 Shader 問題，
            // 而不是丟出 NullReferenceException 讓人以為是別的地方壞掉。
            Debug.LogWarning(
                "[NavigationShaderCompat] 找不到任何可用的 Shader（試過："
                + string.Join("、", candidates)
                + "）。請確認專案的算繪管線設定。");

            return Shader.Find("Hidden/InternalErrorShader");
        }
    }
}
