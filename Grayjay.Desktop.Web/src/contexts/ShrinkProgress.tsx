import { Accessor, createContext, useContext } from "solid-js";

export const ShrinkProgressContext = createContext();
export const useShrinkProgress = () => useContext(ShrinkProgressContext) as Accessor<number>;