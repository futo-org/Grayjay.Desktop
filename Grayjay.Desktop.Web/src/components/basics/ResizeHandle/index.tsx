import { JSX, createEffect, createSignal, onCleanup } from 'solid-js';

interface ResizeHandleProps {
  onResize: (dx: number, dy: number) => void;
  onIsResizingChanged?: (isDragging: boolean) => void;
  style?: JSX.CSSProperties;
  class?: string;
}

const ResizeHandle = (props: ResizeHandleProps) => {
  const [resizing, setResizing] = createSignal(false);
  const [startX, setStartX] = createSignal(0);
  const [startY, setStartY] = createSignal(0);

  createEffect(() => {
    props.onIsResizingChanged?.(resizing());
  });

  const handleMouseDown = (event: MouseEvent) => {
    setResizing(true);
    setStartX(event.pageX);
    setStartY(event.pageY);
    event.stopPropagation();
    event.preventDefault();
  };

  const handleMouseMove = (event: MouseEvent) => {
    if (resizing()) {
      const dx = event.pageX - startX();
      const dy = event.pageY - startY();
      props.onResize(dx, dy);
      setStartX(event.pageX);
      setStartY(event.pageY);
      event.stopPropagation();
      event.preventDefault();
    }
  };

  const handleMouseUp = (event: MouseEvent) => {
    setResizing(false);
    event.stopPropagation();
    event.preventDefault();
  };

  document.addEventListener('mousemove', handleMouseMove);
  document.addEventListener('mouseup', handleMouseUp);
  onCleanup(() => {
    props.onIsResizingChanged?.(false);
    document.removeEventListener('mousemove', handleMouseMove);
    document.removeEventListener('mouseup', handleMouseUp);
  });

  return (
    <div class={props.class} onMouseDown={handleMouseDown} style={{ ...props.style, cursor: 'nwse-resize' }}></div>
  );
};

export default ResizeHandle;