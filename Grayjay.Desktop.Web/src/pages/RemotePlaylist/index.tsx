import { type Component, createResource, createSignal } from 'solid-js';
import { useNavigate, useSearchParams } from '@solidjs/router';
import RemotePlaylistDetailView, { type RemotePlaylistAction } from '../../components/RemotePlaylistDetailView';
import { PlaylistBackend } from '../../backend/PlaylistBackend';
import UIOverlay from '../../state/UIOverlay';
import ExceptionModel from '../../backend/exceptions/ExceptionModel';
import { createResourceDefault } from '../../utility';
import { useVideo } from '../../contexts/VideoProvider';

const RemotePlaylistPage: Component = () => {
  const navigate = useNavigate();
  const video = useVideo();
  const [params, setParams] = useSearchParams();
  const [isLoadingQueue$, setIsLoadingQueue] = createSignal(false);
  const [playlist$, playlistResource] = createResourceDefault(() => params.url, async (url) => {
    try {
      return url ? await PlaylistBackend.playlistLoad(url) : undefined;
    } catch (error: any) {
      if (error && error instanceof ExceptionModel) {
        UIOverlay.overlayError((error as ExceptionModel).replaceTitle("Failed to get playlist details"), {
          back: () => navigate(-1),
          retry: () => playlistResource.refetch()
        });
      }
      throw error;
    }
  });

  const promptConvertToLocalPlaylistAndOpen = () => {
    UIOverlay.overlayConfirm({
      yes: async () => {
        try {
          const id = await PlaylistBackend.convertToLocalPlaylist();
          navigate("/web/playlist?id=" + id);
        } catch (error: unknown) {
          if (error instanceof ExceptionModel) {
            UIOverlay.overlayError(error.replaceTitle("Failed to convert playlist"));
          } else {
            console.error("Failed to convert playlist", error);
            UIOverlay.toastTitled("Failed to convert playlist", error instanceof Error ? error.message : String(error));
          }
        }
      }
    }, "To interact with the playlist the playlist must be converted to a local playlist.");
  };

  const play = async (action: RemotePlaylistAction) => {
    setIsLoadingQueue(true);
    try {
      const videos = await PlaylistBackend.contentsAll();
      let index = 0;
      if (typeof action === "object") {
        if (action.index !== undefined && videos[action.index]?.url === action.url) {
          index = action.index;
        } else {
          index = videos.findIndex(playlistVideo => playlistVideo.url === action.url);
        }
      }

      if (videos.length < 1) {
        UIOverlay.toastTitled("Failed to play playlist", "This playlist has no playable videos");
        return;
      }

      if (index < 0) {
        UIOverlay.toastTitled("Failed to play video", "This video is not available in this playlist");
        return;
      }

      video?.actions?.setQueue(index, videos, false, action === "shuffle");
    } catch (error: unknown) {
      if (error instanceof ExceptionModel) {
        UIOverlay.overlayError(error.replaceTitle("Failed to load playlist"));
      } else {
        console.error("Failed to load playlist", error);
        UIOverlay.toastTitled("Failed to load playlist", error instanceof Error ? error.message : String(error));
      }
    } finally {
      setIsLoadingQueue(false);
    }
  };

  const onInteract = (action?: RemotePlaylistAction) => {
    if (action === undefined) {
      promptConvertToLocalPlaylistAndOpen();
      return;
    }

    if ((playlist$()?.videoCount ?? 0) > 100) {
      UIOverlay.overlayConfirm({
        yes: () => play(action)
      }, "Conversion to local playlist is required for this action");
    } else {
      play(action);
    }
  };

  const [contentsPager$] = createResourceDefault(() => playlist$(), async (playlist) => (!playlist) ? undefined : await PlaylistBackend.contentsPager());
  return (
    <RemotePlaylistDetailView type="Playlist"
      name={playlist$()?.name}
      itemCount={playlist$()?.videoCount}
      pager={contentsPager$()}
      isLoading={!playlist$() || isLoadingQueue$()}
      onInteract={onInteract} />
  );
};

export default RemotePlaylistPage;
