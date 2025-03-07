import { type Component, createEffect, createSignal, Show } from 'solid-js';
import { useSearchParams } from '@solidjs/router';
import { PlaylistsBackend } from '../../backend/PlaylistsBackend';
import { useVideo } from '../../contexts/VideoProvider';
import { IPlaylist } from '../../backend/models/IPlaylist';
import PlaylistDetailView from '../../components/PlaylistDetailView';
import CenteredLoader from '../../components/basics/loaders/CenteredLoader';

const PlaylistPage: Component = () => {
  const video = useVideo();
  const [params, setParams] = useSearchParams();
  const [playlist$, setPlaylist] = createSignal<IPlaylist>();

  let [isLoading$, setIsLoading] = createSignal(true);
  const refetch = async (id?: string) => {
    setIsLoading(true);

    try {
      if (id) {
        const playlist = await PlaylistsBackend.get(id);
        setPlaylist(playlist);
        console.log("set playlist", playlist);
      } else {
        setPlaylist(undefined);
        console.log("set playlist undefined");
      }
    } finally {
      setIsLoading(false);
    }
  };

  createEffect(async () => {
    await refetch(params.id);
  });

  return (
    <Show when={playlist$() && !isLoading$()} fallback={<CenteredLoader />}>
      <PlaylistDetailView type="Playlist"
        id={playlist$()?.id}
        name={playlist$()?.name}
        videos={playlist$()?.videos}
        isLoading={!playlist$()}
        onPlayAll={() => video?.actions?.setQueue(0, playlist$()!.videos, false, false)}
        onShuffleAll={() => video?.actions?.setQueue(0, playlist$()!.videos, false, true)}
        onPlay={(v) => {
          const videos = playlist$()?.videos;
          if (!videos) {
            return;
          }

          video?.actions?.setQueue(videos.findIndex(x => x === v), videos, false, false);
        }}
        onRemove={async (v) => {
            const id = playlist$()?.id;
            const videos = playlist$()?.videos;
            if (!id || !videos) {
              return;
            }
            const index = videos.indexOf(v);
            await PlaylistsBackend.removeContentFromPlaylists(id, index);
            refetch(id);
        }}
        onAddToQueue={(v) => video?.actions?.addToQueue(v)}
        onDownload={() => {}}
        refetch={() => refetch()}
        onDragEnd={async () => {
          const playlist = playlist$();
          if (playlist) {
            await PlaylistsBackend.createOrupdate(playlist);
          }
        }} />
    </Show>

  );
};

export default PlaylistPage;
