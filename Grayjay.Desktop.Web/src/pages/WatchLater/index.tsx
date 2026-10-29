import { type Component, createSignal, onMount } from 'solid-js';
import { useVideo } from '../../contexts/VideoProvider';
import PlaylistDetailView from '../../components/PlaylistDetailView';
import { WatchLaterBackend } from '../../backend/WatchLaterBackend';

const WatchLaterPage: Component = () => {
  const video = useVideo();
  
  onMount(() => {
    video?.actions?.refetchWatchLater();
  });

  return (
    <PlaylistDetailView type="Watch Later"
      name={"Watch Later"}
      videos={video?.watchLater()}
      videoAdds={video?.watchLaterAddTimes()}
      isLoading={!video?.watchLater()}
      onPlayAll={(vs, sortedVideos) => {
        const queue = sortedVideos ?? video?.watchLater() ?? [];
        video?.actions?.setQueue(0, queue, false, false, vs);
      }}
      onShuffleAll={(vs, sortedVideos) => {
        const queue = sortedVideos ?? video?.watchLater() ?? [];
        video?.actions?.setQueue(0, queue, false, true, vs);
      }}
      onPlay={(v, vs, sortedVideos) => {
        const videos = sortedVideos ?? video?.watchLater();
        if (!videos) {
          return;
        }

        video?.actions?.setQueue(videos.findIndex(x => x === v), videos, false, false, vs);
      }}
      onRemove={async (v) => {
        await WatchLaterBackend.remove(v.url);
        video?.actions?.refetchWatchLater();
      }}
      onAddToQueue={(v) => video?.actions?.addToQueue(v)}
      onDownload={() => {}}
      refetch={() => video?.actions?.refetchWatchLater()}
      onDragEnd={async () => {
        const videos = video?.watchLater();
        if (!videos) {
          return;
        }

        for (let i = 0; i < videos.length; i++) {
          videos[i].index = i;
        }
        
        await WatchLaterBackend.changeOrder(videos.map(x=>x.url));
        await video?.actions?.refetchWatchLater();
      }} />
  );
};

export default WatchLaterPage;
