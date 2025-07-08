import { type Component } from "solid-js";
import { LoaderGame, LoaderGameHandle } from "../../components/LoaderGame";

const LoaderGameExamplePage: Component = () => {
  let loader: LoaderGameHandle | undefined;
  const handleReady = (h: LoaderGameHandle) => {
    loader = h;
    loader?.startLoader(10000);
    setTimeout(() => loader?.startLoader(), 5000);
    setTimeout(() => loader?.stopAndResetLoader(), 10000);
    setTimeout(() => loader?.startLoader(10000), 15000);
  };

  return (
    <LoaderGame
      src="https://releases.grayjay.app/loadergame.html"
      onReady={handleReady}
      style={{ width: "100%", height: "300px", "border-radius": "12px" }}
    />
  );
};

export default LoaderGameExamplePage;
