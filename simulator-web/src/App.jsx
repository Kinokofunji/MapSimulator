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
            boxShadow: '0 4px 6px rgba(0,0,0,0.3)'
          }}
        >
          進入模擬平台 🚀
        </button>
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