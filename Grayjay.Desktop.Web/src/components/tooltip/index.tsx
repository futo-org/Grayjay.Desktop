import { createSignal, createEffect, JSX, onCleanup } from 'solid-js';
import { Portal } from 'solid-js/web';

interface TooltipProps {
  text: string;
  children: JSX.Element;
}

const Tooltip = (props: TooltipProps) => {
  const [visible, setVisible] = createSignal(false);
  const [tooltipPosition, setTooltipPosition] = createSignal({ top: 0, left: 0 });

  let triggerRef: HTMLDivElement | undefined;
  let tooltipRef: HTMLDivElement | undefined;

  const updatePosition = () => {
    if (triggerRef && tooltipRef) {
      const triggerRect = triggerRef.getBoundingClientRect();
      const tooltipRect = tooltipRef.getBoundingClientRect();
      setTooltipPosition({
        top: triggerRect.bottom + window.scrollY + 8,
        left: triggerRect.left + window.scrollX + triggerRect.width / 2 - tooltipRect.width / 2,
      });
    }
  };

  createEffect(() => {
    if (visible()) {
      updatePosition();
      window.addEventListener('resize', updatePosition);
      window.addEventListener('scroll', updatePosition);
    } else {
      window.removeEventListener('resize', updatePosition);
      window.removeEventListener('scroll', updatePosition);
    }
    onCleanup(() => {
      window.removeEventListener('resize', updatePosition);
      window.removeEventListener('scroll', updatePosition);
    });
  });

  return (
    <>
      <div
        ref={triggerRef}
        style={{ display: 'inline-block' }}
        onMouseEnter={() => setVisible(true)}
        onMouseLeave={() => setVisible(false)}
      >
        {props.children}
      </div>
      {visible() && (
        <Portal>
          <div
            ref={tooltipRef}
            style={{
              position: 'absolute',
              top: `${tooltipPosition().top}px`,
              left: `${tooltipPosition().left}px`,
              "background-color": '#333',
              color: '#fff',
              padding: '8px 12px',
              "border-radius": '4px',
              "font-size": '0.9rem',
              "white-space": 'nowrap',
              "box-shadow": '0 4px 8px rgba(0, 0, 0, 0.2)',
              "z-index": 6,
            }}
          >
            {props.text}
            <div
              style={{
                position: 'absolute',
                top: '-6px',
                left: '50%',
                transform: 'translateX(-50%)',
                width: '0',
                height: '0',
                "border-left": '6px solid transparent',
                "border-right": '6px solid transparent',
                "border-bottom": '6px solid #333',
              }}
            ></div>
          </div>
        </Portal>
      )}
    </>
  );
};

export default Tooltip;
