import { useState } from "react";
import { Unity, useUnityContext } from "react-unity-webgl";
import './App.css';

function App() {
  // 這裡就是實際「使用」到 useState 的地方！(警告會因為這行而消失)
  const [isStarted, setIsStarted] = useState(false);

  // 載入 Unity 3D 資源的設定
  const { unityProvider } = useUnityContext({
    loaderUrl: "/Build/Build.loader.js",
    dataUrl: "/Build/Build.data",
    frameworkUrl: "/Build/Build.framework.js",
    codeUrl: "/Build/Build.wasm",
  });

  // === 分流邏輯 ===

  // 如果還沒按開始 (isStarted 為 false)，顯示首頁
  if (!isStarted) {
    return (
      // 把原本落落長的 style 換成 className="landing-page"
      <div className="landing-page">
        <h1 style={{ fontSize: '3rem', marginBottom: '10px' }}></h1>
        {/* ... 下面的按鈕和文字維持不變 ... */}
        <p style={{ fontSize: '1.2rem', marginBottom: '40px', color: '#a0a0b0' }}>
          
        </p>
        
        {/* === 按鈕群組區域 === */}
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '20px' }}>
          
          {/* 原本的主按鈕 */}
          <button 
            onClick={() => setIsStarted(true)} 
            style={{ 
              padding: '15px 40px', 
              fontSize: '1.5rem', 
              cursor: 'pointer',
              backgroundColor: '#0f3460',
              color: 'white',
              border: 'none',
              borderRadius: '8px',
              boxShadow: '0 4px 6px rgba(0,0,0,0.3)',
              transition: 'transform 0.2s' // 加入一點小動畫預備
            }}
          >
            進入模擬平台 🚀
          </button>

          {/* 新增的三個副按鈕（水平排列） */}
          <div style={{ display: 'flex', gap: '15px' }}>
            
            {/* 按鈕一 */}
            <button style={{
              padding: '10px 20px',
              fontSize: '1rem',
              cursor: 'pointer',
              backgroundColor: 'rgba(255, 255, 255, 0.1)', // 半透明背景
              color: 'white',
              border: '1px solid rgba(255, 255, 255, 0.5)', // 白色細邊框
              borderRadius: '6px',
            }}>
              進入實景平台
            </button>

            {/* 按鈕二 */}
            <button style={{
              padding: '10px 20px',
              fontSize: '1rem',
              cursor: 'pointer',
              backgroundColor: 'rgba(255, 255, 255, 0.1)',
              color: 'white',
              border: '1px solid rgba(255, 255, 255, 0.5)',
              borderRadius: '6px',
            }}>
              進入AR
            </button>

            {/* 按鈕三 */}
            <a
              href="https://pod-venomous-wikipedia.ngrok-free.dev"
              target="_blank"
              rel="noopener noreferrer"
              style={{
                padding: '10px 20px',
                fontSize: '1rem',
                cursor: 'pointer',
                backgroundColor: 'rgba(255, 255, 255, 0.1)',
                color: 'white',
                border: '1px solid rgba(255, 255, 255, 0.5)',
                borderRadius: '6px',
                textDecoration: 'none',
                display: 'inline-block',
              }}
            >
              進入外送模式 👥
            </a>

          </div>
        </div>
        {/* === 按鈕群組區域結束 === */}
      </div>
    );
  }

  // 如果已經按了開始 (isStarted 為 true)，顯示 3D 畫面
  return (
    <div style={{ width: "100%", height: "100vh", backgroundColor: "black" }}>
      <Unity 
        unityProvider={unityProvider} 
        style={{ width: "100%", height: "100%", border: "none" }} 
      />
    </div>
  );
}

export default App;