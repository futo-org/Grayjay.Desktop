import { Component, createSignal, JSX, Show, createEffect, untrack, batch } from 'solid-js';
import styles from './index.module.css';

interface AnimatedImageProps extends JSX.ImgHTMLAttributes<HTMLImageElement> {
  fadeDuration?: number;
  errorContent?: JSX.Element;
}

const AnimatedImage: Component<AnimatedImageProps> = (props) => {
  const [isLoaded, setIsLoaded] = createSignal(false);
  const [hasError, setHasError] = createSignal(false);
  const [currentSrc, setCurrentSrc] = createSignal(props.src);

  createEffect(() => {
    if (props.src !== untrack(currentSrc)) {
      batch(() => {
        setIsLoaded(false);
        setHasError(false);
        setCurrentSrc(props.src);
      });
    }
  });

  const handleLoad = () => {
    batch(() => {
      setIsLoaded(true);
      setHasError(false);
    });
  };

  const handleError = () => {
    batch(() => {
      setIsLoaded(true);
      setHasError(true);
    });
  };

  const { fadeDuration = 500, errorContent, alt, style, class: className, src: srcProp, ...imgProps } = props;

  return (
    <>
      <Show when={isLoaded() && hasError()}>
        <div
          class={`${styles.visual} ${className || ''}`}
          style={style}
        >
          {errorContent || (
            <span class={`${styles.errorText} ${styles.transitionanim} ${
              isLoaded() && hasError() ? styles.visible : styles.hidden
            }`}>{alt || 'Image unavailable'}</span>
          )}
        </div>
      </Show>
      <Show when={!isLoaded() || !hasError()}>
        <img
          class={`${styles.visual} ${styles.transitionanim} ${
            isLoaded() && !hasError() ? styles.visible : styles.hidden
          } ${
            !isLoaded() ? styles.hidden : ''
          } ${className || ''}`}
          style={style}
          alt={alt}
          onLoad={handleLoad}
          onError={handleError}
          {...imgProps}
          src={currentSrc()}
        />
      </Show>
    </>
  );
};

export default AnimatedImage;