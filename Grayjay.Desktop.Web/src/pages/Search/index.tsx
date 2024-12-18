import { createResource, type Component, Show, createEffect, createSignal, createMemo, For, untrack, batch, on } from 'solid-js';

import styles from './index.module.css';
import ContentGrid from '../../components/containers/ContentGrid';
import { SearchBackend } from '../../backend/SearchBackend';
import { useNavigate, useSearchParams } from '@solidjs/router';
import NavigationBar from '../../components/topbars/NavigationBar';
import { ContentType } from '../../backend/models/ContentType';
import ToggleItemButtonGroup, { ToggleButtonGroupItem } from '../../components/ToggleItemButtonGroup';
import iconPlaylist from '../../assets/icons/icon_nav_playlists.svg';
import iconCreators from '../../assets/icons/icon_nav_creators.svg';
import iconFilters from '../../assets/icons/iconfilters.svg';
import iconVideos from '../../assets/icons/videos.svg';
import CustomButton from '../../components/buttons/CustomButton';
import { Portal } from 'solid-js/web';
import IconButton from '../../components/buttons/IconButton';
import iconClose from '../../assets/icons/icon24_close.svg';
import ScrollContainer from '../../components/containers/ScrollContainer';
import ToggleItemButtonGroupMulti from '../../components/ToggleItemButtonGroupMulti';
import ToggleItemBigButtonGroupMulti, { ToggleBigButtonGroupItemMulti } from '../../components/ToggleItemBigButtonGroupMulti';
import StateGlobal from '../../state/StateGlobal';
import { createResourceDefault } from '../../utility';

const SearchPage: Component = () => {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [filtersDialogVisible$, setFiltersDialogVisible] = createSignal(false);
  const [query$, setQuery] = createSignal(params.q);
  const [searchType$, setSearchType] = createSignal(params.type ? parseInt(params.type) as ContentType : ContentType.MEDIA);
  const [filterValues$, setFilterValues] = createSignal<Record<string, string[]> | undefined>(params.filters ? JSON.parse(params.filters) : undefined);
  const [sortBy$, setSortBy] = createSignal(params.sortBy);
  const [enabledSources$, setEnabledSources] = createSignal<string[]>(params.clientIds ? JSON.parse(params.clientIds) : (StateGlobal.sourceStates$() ?? []).map(v => v.config.id));
  const disabledSources$ = createMemo<string[]>(()=>((StateGlobal.sourceStates$() ?? []).filter(x=>enabledSources$().indexOf(x.config.id) < 0).map(v => v.config.id)));
  let filtersChanged = false;

  createEffect(() => {
    console.log("query changed", params.q);
    setQuery(params.q);
    searchPagerActions.refetch();
  });

  createEffect(() => {
    console.log("type changed", params.type);
    setSearchType(params.type ? parseInt(params.type) as ContentType : ContentType.MEDIA);
    searchPagerActions.refetch();
  });

  const [searchPager, searchPagerActions] = createResourceDefault(async () => {
    console.log("retrieve new pager");
    const query = query$();
    return query ? await SearchBackend.searchPagerLazy(query, untrack(searchType$), untrack(sortBy$), untrack(filterValues$), untrack(disabledSources$)) : undefined;
  });

  const performSearch = (type?: ContentType, sortBy?: string, filters?: Record<string, string[]>, clientIds?: string[]) => {
    const query = query$();
    if (!query) {
      return;
    }

    const newNavigationUri = "/web/search?q=" + encodeURIComponent(query) + (type ? "&type=" + encodeURIComponent(type) : "") + (sortBy ? "&sortBy=" + encodeURIComponent(sortBy) : "") + (filters ? "&filters=" + encodeURIComponent(JSON.stringify(filters)) : "") + (clientIds ? "&clientIds=" + encodeURIComponent(JSON.stringify(clientIds)) : "");
    console.log("navigating to", newNavigationUri);
    navigate(newNavigationUri);
    searchPagerActions.refetch();
  };

  let filtersScrollContainerRef: HTMLDivElement | undefined;

  createEffect(() => {
    setEnabledSources((StateGlobal.sourceStates$() ?? []).map(v => v.config.id));
  });

  const commonCapabilities$ = createMemo(() => {
    const searchCapabilities = StateGlobal.getCommonSearchCapabilities(enabledSources$());
    return searchCapabilities;
  });

  const sourceFilters$ = createMemo(() => {
    const sourceStates = StateGlobal.sourceStates$();
    if (!sourceStates) {
      return [];
    }

    const sourceFilters = sourceStates.map<ToggleBigButtonGroupItemMulti>(s => {
      return { 
        text: s.config.name,
        value: s.config.id, 
        icon: s.config.absoluteIconUrl!
      };
    });

    return sourceFilters;
  });

  createEffect(() => {
    if (!filtersDialogVisible$() && filtersChanged) {
      filtersChanged = false;
      performSearch(untrack(searchType$), untrack(sortBy$), untrack(filterValues$), untrack(enabledSources$));
    }
  });

  createEffect(async () => {
    const caps = commonCapabilities$();
    const filterValues = untrack(filterValues$);
    
    batch(() => {
      if (caps && filterValues) {
        const newFilterValues: { [key: string]: string[] } = {};
        Object.keys(filterValues).forEach(key => {
          const currentFilter = caps.filters.find(filter => filter.id === key);
          
          if (currentFilter) {
            const validValues = currentFilter.filters.map(f => f.value);
            
            if (currentFilter.isMultiSelect) {
              const validArray = (filterValues[key] as string[]).filter(value => validValues.includes(value));
              if (validArray.length > 0) {
                newFilterValues[key] = validArray;
              }
            } else {
              if (validValues.includes(filterValues[key][0])) {
                newFilterValues[key] = filterValues[key];
              }
            }
          }
        });
        
        setFilterValues(newFilterValues);
      } else {
        setFilterValues(undefined);
      }
      
      const sort = untrack(sortBy$);
      if (sort) {
        if (!caps?.sorts.includes(sort)) {
          setSortBy(undefined);
        }
      }

      console.log("Common capabilities changed", { caps, filterValues });
    });
  });
  
  const sortItems$ = createMemo(() => commonCapabilities$()?.sorts.map<ToggleButtonGroupItem>(v => {
    return {
      text: v,
      value: v
    };
  }));

  let scrollContainerRef: HTMLDivElement | undefined;

  return (
    <>
      <div class={styles.container}>
          <NavigationBar initialText={query$()} defaultSearchType={searchType$()} />
          <div style="display: flex; flex-direction: row; align-items: center; margin-bottom: 24px; gap: 24px; margin-left: 24px; margin-right: 24px;">
            <ToggleItemButtonGroup items={[
              { text: "Media", value: ContentType.MEDIA, icon: iconVideos },
              { text: "Creators", value: ContentType.CHANNEL, icon: iconCreators },
              { text: "Playlists", value: ContentType.PLAYLIST, icon: iconPlaylist }
            ]} defaultSelectedValue={searchType$()} onValueChanged={(v) => {
              setSearchType(v);
              performSearch(v, sortBy$(), filterValues$(), enabledSources$());
            }} />
            <Show when={searchType$() === ContentType.MEDIA}>
              <CustomButton text='Filters' icon={iconFilters} border='1px solid #2E2E2E' style={{"height": "44px" }} onClick={() => setFiltersDialogVisible(true)} />
            </Show>
          </div>
          <Show when={searchPager.state == 'ready'}>
            <ScrollContainer ref={scrollContainerRef}>
              <ContentGrid pager={searchPager()} outerContainerRef={scrollContainerRef} />
            </ScrollContainer>
          </Show>
      </div>
      <Portal>
          <Show when={filtersDialogVisible$()}>
            <div class={styles.filtersDialogBackground} onClick={() => setFiltersDialogVisible(false)}>
              <div class={styles.filtersDialog} onClick={(e) => {
                e.preventDefault();
                e.stopPropagation();
              }}>
                <div style="display: flex; align-items: center; width: 100%;">
                  <div class={styles.filtersDialogTitle}>Filters</div>
                  <div style="flex-grow: 1"></div>
                  <IconButton icon={iconClose} height='24px' width='24px' style={{ "margin-left": "24px" }} onClick={() => setFiltersDialogVisible(false)} />
                </div>
                <ScrollContainer ref={filtersScrollContainerRef} style={{ "width": "100%" }} scrollToTopButton={false}>
                  <div class={styles.filterHeader}>Select sources</div>
                  <ToggleItemBigButtonGroupMulti items={sourceFilters$()} defaultSelectedValues={enabledSources$()} onValueChanged={(items) => {
                    setEnabledSources(items);
                    filtersChanged = true; 
                  }} />
                  <Show when={sortItems$() && sortItems$()?.length}>
                    <div class={styles.filterHeader}>Sort by</div>
                    <ToggleItemButtonGroup items={sortItems$()} defaultSelectedValue={sortBy$()} onValueChanged={(item) => {
                      setSortBy(item);
                      filtersChanged = true;
                    }} />
                  </Show>
                  <For each={commonCapabilities$()?.filters}>{(item, i) => {
                    const items$ = createMemo(() => item.filters.map<ToggleButtonGroupItem>(v => {
                      return {
                        text: v.name,
                        value: v.id ?? v.name
                      };
                    }));
                    const selectedValue$ = createMemo(() => {
                        return filterValues$()?.[item.id ?? item.name];
                    });
                    return (
                      <>
                        <div class={styles.filterHeader}>{item.name}</div>
                        <Show when={item.isMultiSelect} fallback={
                          <ToggleItemButtonGroup items={items$()} defaultSelectedValue={selectedValue$()} onValueChanged={(v) => {
                            setFilterValues({ ... filterValues$(), [item.id ?? item.name]: v ? [ v ] : [] });
                            filtersChanged = true;
                          }} />
                        }>
                          <ToggleItemButtonGroupMulti items={items$()} defaultSelectedValues={selectedValue$()} onValueChanged={(v) => {
                            setFilterValues({ ... filterValues$(), [item.id ?? item.name]: v });
                            filtersChanged = true;
                          }} />
                        </Show>
                      </>
                    );
                  }}</For>
                </ScrollContainer>
              </div>
            </div>
          </Show>
      </Portal>
    </>
  );
};

export default SearchPage;
