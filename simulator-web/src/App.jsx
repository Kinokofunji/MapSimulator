import React from "react";
import { Unity, useUnityContext } from "react-unity-webgl";
//import "./App.css";

function App() {
  // 這裡的網址對應的就是你取消壓縮後的純淨檔案
  const { unityProvider } = useUnityContext({
    loaderUrl: "/Build/Build_WebGL.loader.js",
    dataUrl: "/Build/Build_WebGL.data",
    frameworkUrl: "/Build/Build_WebGL.framework.js",
    codeUrl: "/Build/Build_WebGL.wasm",
  });

  return (
    <div style={{ display: "flex", flexDirection: "column", alignItems: "center", marginTop: "50px" }}>
      <h1>沉浸式實景地圖平台</h1>
      <p>第一台 3D 車輛登場！</p>
      
      <div style={{ border: "2px solid #ccc", borderRadius: "8px", overflow: "hidden" }}>
        <Unity unityProvider={unityProvider} style={{ width: "960px", height: "600px" }} />
      </div>
    </div>
  );
}

export default App;