import { Component, createEffect } from "solid-js";
import { CustomDialogLocal } from "../OverlayRoot";
import OverlayFilePicker from "../OverlayFilePicker";

export interface OverlayFilePickerProps {
  dialog: CustomDialogLocal
}

const OverlayRemoteFilePicker: Component<OverlayFilePickerProps> = (props) => {
  createEffect(() => console.info("OverlayRemoteFilePicker dialog changed", props.dialog));
  createEffect(() => console.info("OverlayRemoteFilePicker data changed", props.dialog.data$()));
  return (
    <OverlayFilePicker 
      allowMultiple={props.dialog.data$().AllowMultiple} 
      defaultFileName={props.dialog.data$().DefaultFileName} 
      filters={props.dialog.data$().Filters} 
      mode={props.dialog.data$().Mode}
      selectionMode={props.dialog.data$().SelectionMode}
      onPick={(v) => {
        props.dialog.action!('pick', v);
      }} />
  );
};

export default OverlayRemoteFilePicker;