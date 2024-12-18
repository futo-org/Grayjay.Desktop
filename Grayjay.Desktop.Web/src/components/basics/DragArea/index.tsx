import { JSX, createEffect, createSignal, onCleanup } from 'solid-js';

interface DragAreaProps {
  onDrag: (dx: number, dy: number) => void;
  onIsDraggingChanged?: (isDragging: boolean) => void;
  style?: JSX.CSSProperties;
  class?: string;
}

const DragArea = (props: DragAreaProps) => {
  const [dragging, setDragging] = createSignal(false);
  const [startX, setStartX] = createSignal(0);
  const [startY, setStartY] = createSignal(0);

  createEffect(() => {
    props.onIsDraggingChanged?.(dragging());
  });

  const handleMouseDown = (event: MouseEvent) => {
    setDragging(true);
    setStartX(event.pageX);
    setStartY(event.pageY);
    event.stopPropagation();
    event.preventDefault();
  };

  const handleMouseMove = (event: MouseEvent) => {
    if (dragging()) {
      const dx = event.pageX - startX();
      const dy = event.pageY - startY();
      props.onDrag(dx, dy);
      setStartX(event.pageX);
      setStartY(event.pageY);
      event.stopPropagation();
      event.preventDefault();
    }
  };

  const handleMouseUp = (event: MouseEvent) => {
    setDragging(false);
    event.stopPropagation();
    event.preventDefault();
  };

  document.addEventListener('mousemove', handleMouseMove);
  document.addEventListener('mouseup', handleMouseUp);
  onCleanup(() => {
    props?.onIsDraggingChanged?.(false);
    document.removeEventListener('mousemove', handleMouseMove);
    document.removeEventListener('mouseup', handleMouseUp);
  });

  return (
    <div class={props.class} onMouseDown={handleMouseDown} style={{ ... props.style, cursor: 'grab' }}></div>
  );
};

export default DragArea;