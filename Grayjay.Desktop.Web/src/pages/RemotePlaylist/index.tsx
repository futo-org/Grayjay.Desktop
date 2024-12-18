import { type Component, createResource } from 'solid-js';
import { useNavigate, useSearchParams } from '@solidjs/router';
import RemotePlaylistDetailView from '../../components/RemotePlaylistDetailView';
import { PlaylistBackend } from '../../backend/PlaylistBackend';
import UIOverlay from '../../state/UIOverlay';
import ExceptionModel from '../../backend/exceptions/ExceptionModel';
import { createResourceDefault } from '../../utility';

const RemotePlaylistPage: Component = () => {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
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
        const id = await PlaylistBackend.convertToLocalPlaylist();
        navigate("/web/playlist?id=" + id);
      }
    }, "To interact with the playlist the playlist must be converted to a local playlist.");
  };

  const [contentsPager$] = createResourceDefault(() => playlist$(), async (playlist) => (!playlist) ? undefined : await PlaylistBackend.contentsPager());
  return (
    <RemotePlaylistDetailView type="Playlist"
      name={playlist$()?.name}
      itemCount={playlist$()?.videoCount}
      pager={contentsPager$()}
      isLoading={!playlist$()}
      onInteract={promptConvertToLocalPlaylistAndOpen} />
  );
};

export default RemotePlaylistPage;
