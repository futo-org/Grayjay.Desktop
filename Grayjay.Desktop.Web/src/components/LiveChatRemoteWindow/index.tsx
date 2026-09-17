import { Component, JSX, createEffect, onCleanup } from 'solid-js';
import styles from './index.module.css';
import { ILiveChatWindowDescriptor } from '../../backend/models/comments/ILiveChatWindowDescriptor';
import { DetailsBackend } from '../../backend/DetailsBackend';

interface LiveChatRemoteWindowProps {
    style?: JSX.CSSProperties;
    descriptor: ILiveChatWindowDescriptor;
}

const LiveChatRemoteWindow: Component<LiveChatRemoteWindowProps> = (props) => {
    let container: HTMLDivElement | undefined;

    createEffect(() => {
        const descriptor = props.descriptor;
        if (!container || !/^https?:\/\//i.test(descriptor.url)) return;
        const registry = window.customElements;
        if (!registry?.get('justcef-view')) {
            console.error('Live chat view is unavailable because justcef-view failed to initialize');
            return;
        }
        const element = document.createElement('justcef-view');
        element.className = styles.view;
        element.setAttribute('aria-label', 'Live chat');
        element.setAttribute('tabindex', '0');
        element.setAttribute('src', 'about:blank');
        let disposed = false;
        element.addEventListener('viewcreated', async (event) => {
            try {
                const viewId = (event as CustomEvent<{ viewId: number }>).detail.viewId;
                console.info('[Grayjay live chat cleanup] Configuring view', { viewId, descriptor });
                await DetailsBackend.configureLiveChatView(viewId, descriptor);
                if (!disposed) {
                    console.info('[Grayjay live chat cleanup] Script registered and navigation started; see the chat view console for selector results', { viewId });
                }
            } catch (error) {
                if (!disposed) console.error('Failed to initialize live chat', error);
            }
        }, { once: true });
        element.addEventListener('error', (event) => {
            if (!disposed) console.error('Live chat view failed', (event as CustomEvent).detail);
        });
        container.append(element);
        onCleanup(() => {
            disposed = true;
            element.remove();
        });
    });

    return <div ref={container} class={styles.container} style={props.style} />;
};

export default LiveChatRemoteWindow;
