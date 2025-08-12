import { FocusableOptions, ScopeOptions } from "./nav";

declare module "solid-js" {
  namespace JSX {
    interface Directives {
      focusable: FocusableOptions;
      focusScope: ScopeOptions;
    }
  }
}