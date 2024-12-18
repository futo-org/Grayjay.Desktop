import { type Component } from 'solid-js';
import ScrollContainer from '../../components/containers/ScrollContainer';
import FlexibleArrayList from '../../components/containers/FlexibleArrayList';

const VirtualExamplePage: Component = () => {
  const flexibleItems = [ ... Array(100).keys() ].map((i) => `${i} Lorem ipsum dolor sit amet, consectetur adipiscing elit. Etiam rhoncus enim ante, non fermentum est aliquam a. Nunc auctor ac magna et tristique. Fusce tellus sapien, luctus faucibus cursus eu, fringilla sed dui. Mauris tincidunt cursus ante, id feugiat sem iaculis et. Donec nisi dolor, congue sed arcu et, fringilla efficitur quam. Mauris eget leo eget massa sollicitudin sagittis vel sed neque. Ut ultricies efficitur magna, eget lobortis neque. Pellentesque habitant morbi tristique senectus et netus et malesuada fames ac turpis egestas. Sed in tellus tellus. Nulla convallis ante quis luctus vestibulum. Mauris vel justo laoreet, pretium est quis, luctus eros. Nam nec mi non purus dapibus congue. Aenean laoreet blandit dui, non ornare lacus tristique eget. Aenean dignissim viverra porttitor. Duis non gravida augue, ac vestibulum purus. Phasellus id tellus maximus nulla suscipit consectetur eget vel quam.`);
  let scrollContainerRef: HTMLDivElement | undefined;
    return (
    <ScrollContainer ref={scrollContainerRef}>
      <div style="height: 200px"></div>
      <FlexibleArrayList outerContainerRef={scrollContainerRef} 
        items={flexibleItems}
        builder={(index, item) => 
          <div style={{"background-color":"gray"}}>
            {index()}: {item()}
          </div>
        } />
    </ScrollContainer>
  );
};

export default VirtualExamplePage;
