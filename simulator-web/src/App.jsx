import React from "react";
import { Unity, useUnityContext } from "react-unity-webgl";
//import "./App.css";

function App() {
  // 這裡的網址對應的就是你取消壓縮後的純淨檔案
  const { unityProvider } = useUnityContext({
    loaderUrl: "/Build/Build.loader.js",
    dataUrl: "/Build/Build.data",
    frameworkUrl: "/Build/Build.framework.js",
    codeUrl: "/Build/Build.wasm",
  });

  return (
    <div style={{ display: "flex", flexDirection: "column", alignItems: "center", marginTop: "50px" }}>
      <h1>沉浸式實景地圖平台</h1>
      <p>第一台 3D 車輛登場！</p>
      
      <div style={{ border: "2px solid #ccc", borderRadius: "8px", overflow: "hidden" }}>
        <Unity 
  unityProvider={unityProvider} 
  style={{ 
    width: "100%", 
    height: "calc(100vh - 60px)", /* 100vh 代表螢幕的可視高度 */
    // 如果你們有網頁標題列，可以改成 height: "calc(100vh - 60px)" 之類的來扣掉標題高度
    border: "none" 
  }} 
/>
      </div>
    </div>
  );
}

export default App;