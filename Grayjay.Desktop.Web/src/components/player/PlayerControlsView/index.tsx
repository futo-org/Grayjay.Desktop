import { Component, For, Index, JSX, Show, createMemo, createSignal, onCleanup, onMount, untrack } from "solid-js";
import styles from './index.module.css';
import { DateTime, Duration } from "luxon";
import play from '../../../assets/icons/icon_32_play.svg';
import pause from '../../../assets/icons/icon32_pause.svg';
import ic_volume from '../../../assets/icons/ic_volume.svg';
import ic_mute from '../../../assets/icons/ic_mute.svg';
import fullscreen from '../../../assets/icons/icon_32_fullscreen.svg';
import cast from '../../../assets/icons/icon_32_cast.svg';
import settings from '../../../assets/icons/icon_32_settings.svg';
import minimize from '../../../assets/icons/icon_32_minimize.svg';
import iconTheatre from '../../../assets/icons/icon_theatre.svg';
import { dateFromAny, formatDuration } from "../../../utility";
import { useCasting } from "../../../contexts/Casting";
import { Event0 } from "../../../utility/Event";
import { ChapterType, IChapter } from "../../../backend/models/contentDetails/IChapter";
import { IPlatformVideoDetails } from "../../../backend/models/contentDetails/IPlatformVideoDetails";

export interface PlayerControlsProps {
    duration: Duration;
    position: Duration;
    positionBuffered: Duration;
    isPlaying: boolean;
    isLive?: boolean;
    video?: IPlatformVideoDetails,
    onSkip: ()=>void;
    onSetScrubbing?: (scrubbing: boolean) => void;
    onSetPosition?: (position: Duration) => void;
    onSetIsChangingVolume?: (isChangingVolume: boolean) => void;
    onSetVolume?: (volume: number) => void;
    handleFullscreen?: () => void;
    handleEscape?: () => void;
    handleMinimize?: () => void;
    handlePause?: ()=>void;
    handlePlay?: ()=>void;
    handleTheatre?: () => void;
    handleToggleVolume?: () => void;
    onInteraction?: () => void;
    children: JSX.Element;
    volume?: number;
    buttons?: JSX.Element;
    handleSettingsMenu?: (el: HTMLElement|undefined) => void;
    eventMoved?: Event0;
    chapters?: IChapter[];
    leftButtonContainerStyle?: JSX.CSSProperties;
    rightButtonContainerStyle?: JSX.CSSProperties;
};

const PlayerControlsView: Component<PlayerControlsProps> = (props) => {
    const casting = useCasting();

    let progressBar: HTMLDivElement | undefined;
    let volumeBar: HTMLDivElement | undefined;
    let settingsButton: HTMLElement | undefined;
    let containerRef: HTMLDivElement | undefined;
    let scrubbing = false;
    let changingVolume = false;

    const [progressBarChapterHovering$, setProgressBarChapterHovering] = createSignal<number>();

    const [progressBarDimensions, setProgressBarDimensions] = createSignal({left: 0, width: 0, offsetLeft: 0});
    const calculateWidth = (current: Duration, total: Duration, progressBarWidth: number) => {
        return total.as('milliseconds') === 0 
            ? 0 
            : Math.min(1, (current.as('milliseconds') / total.as('milliseconds'))) * progressBarWidth;
    };

    const startScrubbing = (e: MouseEvent) => {
        scrubbing = true;
        props.onSetScrubbing?.(scrubbing);
        props.onInteraction?.();
        e.stopPropagation();
        e.preventDefault();
    };

    const startChangingVolume = (e: MouseEvent) => {
        changingVolume = true;
        props.onSetIsChangingVolume?.(changingVolume);
        props.onInteraction?.();
        e.stopPropagation();
        e.preventDefault();
    };

    const scrub = (e: MouseEvent) => {
        if (scrubbing && progressBar) {
            const d = untrack(progressBarDimensions);
            const positionFraction = (e.x - d.left) / d.width;
            const scrubPosition = Duration.fromMillis(positionFraction * props.duration.as("milliseconds"));
            props.onSetPosition?.(scrubPosition);
            props.onInteraction?.();
        }
    };

    const changeVolume = (e: MouseEvent) => {
        if (changingVolume && volumeBar) {
            const boundingRect = volumeBar?.getBoundingClientRect();
            const volume = (e.x - boundingRect.left) / 72;
            props.onSetVolume?.(Math.min(Math.max(0, volume), 1));
            props.onInteraction?.();
        }
    };

    const stopScrubbing = (e: MouseEvent) => {
        if (scrubbing && progressBar) {
            scrub(e);
            scrubbing = false;
            props.onSetScrubbing?.(scrubbing);
            props.onInteraction?.();
        }
        e.stopPropagation();
        e.preventDefault();
    };

    const stopChangingVolume = (e: MouseEvent) => {
        if (changingVolume && volumeBar) {
            changeVolume(e);
            changingVolume = false;
            props.onSetIsChangingVolume?.(changingVolume);
            props.onInteraction?.();
        }
        e.stopPropagation();
        e.preventDefault();
    };

    const onMouseOut = (e: MouseEvent) => {
        stopScrubbing(e);
        stopChangingVolume(e);
        setProgressBarChapterHovering(-1);
    }
    const onMouseMove = (e: MouseEvent) => {
        scrub(e);
        changeVolume(e);
        props.onInteraction?.();
        e.stopPropagation();
        e.preventDefault();
        
        //Required due to mutually exclusive HTML behavior of hover & mouse events.
        const interactiveContainer = e.target as HTMLElement;
        const interactiveBounds = interactiveContainer.getBoundingClientRect();
        const middleY = interactiveBounds.top + interactiveBounds.height / 2;
        const elements = document.elementsFromPoint(e.clientX, middleY);
        const chapterElement = elements.find(x=>x.classList.contains(styles.progressBarChapter));
        const chapterIndex = (!chapterElement) ? -1 : Array.prototype.indexOf.call(chapterElement.parentNode?.children, chapterElement);
        setProgressBarChapterHovering(chapterIndex);
    };

    const onPause = (e: MouseEvent) => {
        if(props.handlePause)
            props.handlePause();
        
        e.stopPropagation();
        e.preventDefault();
    };

    const onToggleVolume = (e: MouseEvent) => {
        if(props.handleToggleVolume)
            props.handleToggleVolume();
        
        e.stopPropagation();
        e.preventDefault();
    };
    const onPlay = (e: MouseEvent) => {
        if(props.handlePlay)
            props.handlePlay();
        
        e.stopPropagation();
        e.preventDefault();
    };

    const onFullscreen = (e: MouseEvent) => {
        if (props.handleFullscreen)
            props.handleFullscreen();
        e.stopPropagation();
        e.preventDefault();
    };

    const onTheatre = (e: MouseEvent) => {
        if (props.handleTheatre)
            props.handleTheatre();
        e.stopPropagation();
        e.preventDefault();
    };

    const onSettings = (e: MouseEvent) => {
        e.stopPropagation();
        e.preventDefault();

        props.handleSettingsMenu?.(settingsButton);
    };

    const onPopOut = (e: MouseEvent) => {
        e.stopPropagation();
        e.preventDefault();
    };

    const onCast = (e: MouseEvent) => {
        casting?.actions.open();
        e.stopPropagation();
        e.preventDefault();
    };

    const updateProgressBar = () => {
        const boundingRect = progressBar?.getBoundingClientRect();
        setProgressBarDimensions({
            width: boundingRect?.width ?? 0,
            left: boundingRect?.left ?? 0,
            offsetLeft: progressBar?.offsetLeft ?? 0,
        });
    };

    requestAnimationFrame(() => {
        updateProgressBar();
    });

    const resizeObserver = new ResizeObserver(entries => {
        updateProgressBar();
    });

    onMount(() => {
        document.addEventListener("keydown", handleKeyDown);
        document.addEventListener("keyup", handleKeyUp);

        resizeObserver.observe(containerRef!);
        props.eventMoved?.register(() => {
            updateProgressBar();
        }, this);
    });

    onCleanup(() => {
        document.removeEventListener("keydown", handleKeyDown);
        document.removeEventListener("keyup", handleKeyUp);
        resizeObserver.unobserve(containerRef!);
        props.eventMoved?.unregister(this);
        resizeObserver.disconnect();
    });

    const currentChapter$ = createMemo(()=>{
        return props.chapters?.find(x=>x.timeStart < (props.position.milliseconds / 1000) && x.timeEnd > (props.position.milliseconds / 1000));
    });
    const currentChapterProgress$ = createMemo(()=>{
        const currentChapter = currentChapter$();
        if(currentChapter) {
            const progress = (props.position.milliseconds / 1000) - currentChapter.timeStart;
            return progress / (currentChapter.timeEnd - currentChapter.timeStart); 
        }
        return 0;
    });
    const isSkippable$ = createMemo(()=>{
        const chapter = currentChapter$();
        if(!chapter)
            return false;
        return chapter.type == ChapterType.SKIP || chapter.type == ChapterType.SKIPPABLE;
    });

    //TODO: Fix font on duration timestamp
    //TODO: onMouseUp is not properly working
    //TODO: Ommit hours when hours are 0
    //TODO: Double click fullscreen
    //TODO: Convert button to button lists left and right (41px from right, 32px space between buttons)

    //TODO: Just use percentages positions/widths with a correct outer container? width = (position / duration * 100) + "%";
    //TODO: Requires no memos or re-calculations on resizing.
    const progressWidth = createMemo(() => calculateWidth(props.position, props.duration, progressBarDimensions().width));
    const bufferWidth = createMemo(() => calculateWidth(props.positionBuffered, props.duration, progressBarDimensions().width));
    const progressHandleLeft = createMemo(() => progressWidth() + progressBarDimensions().offsetLeft);

    const volumeWidth = createMemo(() => (props.volume ?? 0) * 72);

    const skip = (direction: number) => {
        let position = props.position.as("milliseconds");
        const duration = props.duration.as("milliseconds");
        if (duration > 0) {
            position = Math.max(Math.min(duration, position + direction * 5000), 0);
        } else {
            position = Math.max(position + direction * 5000, 0);
        }
        props.onSetPosition?.(Duration.fromMillis(position));
    };

    const handleKeyDown = (ev: KeyboardEvent) => {
        const target = ev.target as any;
        if (target) {
            const isInputField = target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT';
            if (isInputField) {
                return;
            }
        }

        switch (ev.key) {
            case " ":
            case "k":
                if (props.isPlaying) {
                    onPause(ev as any);
                } else {
                    onPlay(ev as any);
                }
                props.onInteraction?.();
                ev.preventDefault();
                break;
            /*case "ArrowRight":
                startSkipping(1);
                props.onInteraction?.();
                ev.preventDefault();
                break;
            case "ArrowLeft":
                startSkipping(-1);
                props.onInteraction?.();
                ev.preventDefault();
                break;
            case "ArrowUp":
                props.onSetVolume?.(Math.min((props.volume ?? 0) + 0.1, 1));
                props.onInteraction?.();
                ev.preventDefault();
                break;
            case "ArrowDown":
                props.onSetVolume?.(Math.max((props.volume ?? 0) - 0.1, 0));
                props.onInteraction?.();
                ev.preventDefault();
                break;*/
            case "f":
                onFullscreen(ev as any);
                props.onInteraction?.();
                ev.preventDefault();
                break;
            case "t":
                onTheatre(ev as any);
                props.onInteraction?.();
                ev.preventDefault();
                break;
            case "m":
                onToggleVolume(ev as any);
                props.onInteraction?.();
                ev.preventDefault();
                break;
            case "Home":
                props.onSetPosition?.(Duration.fromMillis(0));
                props.onInteraction?.();
                ev.preventDefault();
                break;
            case "End":
                props.onSetPosition?.(props.duration);
                props.onInteraction?.();
                ev.preventDefault();
                break;
            case "i":
                props.handleMinimize?.();
                props.onInteraction?.();
                ev.preventDefault();
                break;
        }
    };

    const handleKeyUp = (ev: KeyboardEvent) => {
        switch (ev.key) {
            /*case "ArrowRight":
            case "ArrowLeft":
                stopSkipping();
                props.onInteraction?.();
                ev.preventDefault();
                break;*/
            case "Escape":
                props.handleEscape?.();
                props.onInteraction?.();
                ev.preventDefault();
                break;
        }
    };

    let startSkippingTimeout: NodeJS.Timeout | undefined;
    let skipInterval: NodeJS.Timeout | undefined;
    const startSkipping = (direction: number) => {
        stopSkipping();

        skip(direction);
        startSkippingTimeout = setTimeout(() => {
            skipInterval = setInterval(() => {
                skip(direction);
            }, 100);
        }, 2000);
    };

    const stopSkipping = () => {
        if (startSkippingTimeout) {
            clearTimeout(startSkippingTimeout);
            startSkippingTimeout = undefined;
        }

        if (skipInterval) {
            clearInterval(skipInterval);
            skipInterval = undefined;
        }
    };

    let progressBarContainer!: HTMLDivElement;

    function calculateTranslateX(chapter) {
        const left = (Math.max(1, chapter.timeStart) / (props.duration.milliseconds / 1000)) * progressBarContainer.offsetWidth + 2;
        let width = (((Math.min((props.duration.milliseconds / 1000), chapter.timeEnd - chapter.timeStart)) / (props.duration.milliseconds / 1000))) * progressBarContainer.offsetWidth + 2;
        if(width > progressBarContainer.offsetWidth - 4)
            width = progressBarContainer.offsetWidth - left;
        const labelWidth = (11 * (chapter.name.length) / 2);   
        const leftLabelCenter = left + (width / 2);
        if((leftLabelCenter - labelWidth/2) > 0) {
            const overflowWidth = Math.max(0, (leftLabelCenter + labelWidth/2) - (progressBarContainer.offsetWidth));
            if(overflowWidth > 0) {
                let percentage = overflowWidth / labelWidth;
                return -50 - Math.round(percentage * 100) - 1;
            }
        }
        else {
            const overflowWidth = (leftLabelCenter - labelWidth/2);
            if(overflowWidth < 0) {
                let percentage = (overflowWidth) / labelWidth * -1;
                return -50 + Math.floor(percentage * 100) + 1;
            }
        }
        return -50;
    }

    return (
        <div ref={containerRef} class={styles.container}>
            <Show when={isSkippable$()}>
                <div class={styles.skipButton} onClick={props.onSkip}>
                    Skip
                </div>
            </Show>
            <div class={styles.pauseArea} onClick={onPause}>

            </div>
            <div ref={progressBar} class={styles.progressBar} />
            <div class={styles.progressBarBuffer} style={{ width: `${bufferWidth()}px` }} />
            <div class={styles.progressBarProgress} style={{ width: `${progressWidth()}px` }} />
            <div class={styles.progressBarContainer} ref={progressBarContainer}>
                <Show when={(props.chapters?.length ?? 0) > 0}>
                    <div class={styles.progressBarChapters}>
                        <Index each={props.chapters}>{ (chapter$, i: number) =>
                            <div
                                classList={{
                                    [styles.progressBarChapter]: true, 
                                    [styles.hover]: progressBarChapterHovering$() == i,
                                    
                                }} 
                                style={{
                                    left: `calc(${(Math.max(1, chapter$().timeStart) / (props.duration.milliseconds / 1000)) * 100}% + 2px)`,
                                    width: `calc(${((Math.min((props.duration.milliseconds / 1000), chapter$().timeEnd - chapter$().timeStart)) / (props.duration.milliseconds / 1000)) * 100}% - 2px)`,
                                }}>
                                    <div class={styles.label} style={{
                                        transform: "translateX(" + calculateTranslateX(chapter$()) + "%)"
                                    }}>
                                        {chapter$().name}
                                    </div>
                                    <Show when={progressBarChapterHovering$() == i}>
                                        <div class={styles.hoverBar} 
                                            style={{
                                                width: (chapter$() == currentChapter$()) ? `calc(${currentChapterProgress$() * 100}%)` : ``
                                            }}
                                            classList={{
                                                [styles.pastProgress]: (chapter$().timeStart < (props.position.milliseconds / 1000)),
                                                [styles.partial]: chapter$() == currentChapter$()
                                            }}>
                                        </div>
                                        <Show when={(chapter$() == currentChapter$())}>
                                            <div class={styles.hoverBarPartial} style={{
                                                left: (chapter$() == currentChapter$()) ? `calc(${(currentChapterProgress$()) * 100}%)` : ``,
                                                width: (chapter$() == currentChapter$()) ? `calc(${(1 - currentChapterProgress$()) * 100}%)` : ``
                                            }}>

                                            </div>
                                        </Show>
                                    </Show>
                            </div>
                        }</Index>
                    </div>
                </Show>
            </div>
            <div class={styles.progressBarHandle} style={{ left: `${progressHandleLeft()}px` }} />
            <div class={styles.progressBarInteractiveArea} onMouseDown={startScrubbing} onMouseOut={onMouseOut} onMouseUp={stopScrubbing} onMouseMove={onMouseMove} />

            <div class={styles.leftButtonContainer} style={props.leftButtonContainerStyle}>
                <img src={play} class={styles.play} alt="play" style={{display: !props.isPlaying ? "block" : "none" }} onClick={(ev)=>onPlay(ev)} />
                <img src={pause} class={styles.pause} alt="pause" style={{display: props.isPlaying ? "block" : "none" }} onClick={(ev)=>onPause(ev)} />
                <img src={props.volume ? ic_volume : ic_mute} class={styles.volume} alt="volume" onClick={(ev)=> onToggleVolume(ev)} />
                <Show when={props.volume !== undefined}>
                    <div style="position: relative; height: 24px; width: 92px; flex-shrink: 0">
                        <div ref={volumeBar} class={styles.volumeBar} />
                        <div class={styles.volumeBarProgress} style={{ width: `${volumeWidth()}px` }} />
                        <div class={styles.volumeBarHandle} style={{ left: `${volumeWidth()}px` }} />
                        <div class={styles.volumeBarInteractiveArea} onMouseDown={startChangingVolume} onMouseOut={stopChangingVolume} onMouseUp={stopChangingVolume} onMouseMove={onMouseMove} />
                    </div>
                </Show>

                <div class={styles.positionContainer}>
                    <div class={styles.position}>{formatDuration(props.position)}</div>
                    <Show when={props.video?.isLive && dateFromAny(props.video?.dateTime, DateTime.min())! <= DateTime.now()}>
                        <div class={styles.liveBadge}>LIVE</div>
                    </Show>
                    <Show when={props.video?.isLive && dateFromAny(props.video?.dateTime, DateTime.min())! > DateTime.now()}>
                        <div class={styles.plannedBadge}>PLANNED</div>
                    </Show>
                    <Show when={!props.video?.isLive}>
                        <div class={styles.duration}>/&nbsp&nbsp{formatDuration(props.duration)}</div>
                    </Show>
                    <Show when={currentChapter$()}>
                        <div class={styles.currentChapter}>
                            • {currentChapter$()?.name}
                        </div>
                    </Show>
                </div>
            </div>

            <div class={styles.buttonContainer} style={props.rightButtonContainerStyle}>
                <Show when={props.handleFullscreen}>
                    <img src={fullscreen} class={styles.fullscreen} alt="fullscreen" onClick={onFullscreen} />
                </Show>
                <Show when={props.handleTheatre}>
                    <img src={iconTheatre} class={styles.theatre} alt="theatre" onClick={onTheatre} />
                </Show>
                <img src={cast} class={styles.cast} alt="cast" onClick={onCast} />
                <img ref={(el)=>settingsButton = el} src={settings} class={styles.settings} alt="settings" onClick={(ev)=>onSettings(ev)} />
                {props.buttons}
            </div>
            {props.children}
        </div>
    );
};

export default PlayerControlsView;
