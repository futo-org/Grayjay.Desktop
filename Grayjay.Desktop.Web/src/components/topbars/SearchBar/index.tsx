import { For, Match, Show, Switch, batch, createEffect, createSignal, untrack, type Component, type JSX } from 'solid-js';

import { useNavigate } from '@solidjs/router';
import InputText from '../../basics/inputs/InputText';
import search from '../../../assets/icons/search.svg';
import styles from './index.module.css';
import CustomButton from '../../buttons/CustomButton';

import iconPlaylist from '../../../assets/icons/icon_nav_playlists.svg';
import iconCreators from '../../../assets/icons/icon_nav_creators.svg';
import iconVideos from '../../../assets/icons/videos.svg';
import iconSearch from '../../../assets/icons/icon24_search.svg';
import iconClose from '../../../assets/icons/icon24_close.svg';
import iconAddToQuery from '../../../assets/icons/add_to_query.svg';
import { SearchBackend } from '../../../backend/SearchBackend';
import LoaderSmall from '../../basics/loaders/LoaderSmall';
import { ContentType } from '../../../backend/models/ContentType';
import ScrollContainer from '../../containers/ScrollContainer';

interface SearchBarProps {
  placeholder?: string;
  initialText?: string;
  style?: JSX.CSSProperties;
  inputStyle?: JSX.CSSProperties;
  overlayStyle?: JSX.CSSProperties;
  defaultSearchType?: ContentType;
  onSearch?: (query: string, type?: ContentType) => void;
}

const SearchBar: Component<SearchBarProps> = (props) => {
  const navigate = useNavigate();

  const buttonStyle: JSX.CSSProperties = {
    "border-radius": "6px",
    "border": "1px solid var(--grey-color-7)",
    "height": "37px",
    "padding-left": "16px",
    "padding-right": "16px",
    "font-size": "12px",
    "font-style": "normal",
    "font-weight": 400
  };  

  const buttonIconStyle: JSX.CSSProperties = {
    "width": "16px",
    "height": "16px",
    "margin-right": "4px"
  };

  const [query$, setQuery] = createSignal<string>(props.initialText ?? "");
  const [suggestionItems$, setSuggestionItems] = createSignal<string[]>();
  const [searchHasFocus$, setSearchHasFocus] = createSignal(false);
  const [suggestionsVisible$, setSuggestionsVisible] = createSignal(false);
  const [historical$, setHistorical] = createSignal(false);
  const [isLoadingSuggestions$, setIsLoadingSuggestions] = createSignal(false);
  const [searchType$, setSearchType] = createSignal<ContentType>(props.defaultSearchType ?? ContentType.MEDIA);
  createEffect(() => setSearchType(props.defaultSearchType ?? ContentType.MEDIA));
  createEffect(() => setQuery(props.initialText ?? ""));

  createEffect(() => {
    if (searchHasFocus$()) {
      setSuggestionsVisible(true);
    } else {
      setSuggestionsVisible(false);
    }
  });

  let suggestionCounter = 0;
  const getSuggestions = async (query?: string) => {
    if (query && query.length >= 1) {
      const suggestionIndex = ++suggestionCounter;
      setIsLoadingSuggestions(true);

      let suggestions: string[];
      try {
        suggestions = await SearchBackend.searchSuggestions(query);
        if (suggestionIndex !== suggestionCounter) {
          return;
        }
      } finally {
        setIsLoadingSuggestions(false);
      }

      batch(() => {
        setHistorical(false);
        setSuggestionItems(suggestions);
      });
    } else {
      const suggestionIndex = ++suggestionCounter;
      setIsLoadingSuggestions(true);

      let previousSuggestions: string[];
      try {      
        previousSuggestions = await SearchBackend.previousSearches();
        if (suggestionIndex !== suggestionCounter) {
          return;
        }
      } finally {
        setIsLoadingSuggestions(false);
      }

      batch(() => {
        setHistorical(true);
        setSuggestionItems(previousSuggestions);
      });
    }
  };

  createEffect(() => {
    getSuggestions(query$());
  });

  const searchFor = async (query: string, type?: ContentType) => {
    setSuggestionsVisible(false);
    const currentPath = window.location.pathname;
    let searchParams = new URLSearchParams(window.location.search);
    
    if (currentPath === "/web/search") {
      searchParams.set("q", query);
      searchParams.set("type", (type ?? ContentType.MEDIA).toString());
    } else {
      searchParams = new URLSearchParams();
      searchParams.append("q", query);
      searchParams.append("type", (type ?? ContentType.MEDIA).toString());
    }
    
    navigate("/web/search?" + searchParams.toString(), {});
    props.onSearch?.(query, type);
    await SearchBackend.addPreviousSearch(query);
  };

  const changeSearchType = (e: MouseEvent, type: ContentType) => {
    e.preventDefault();
    e.stopPropagation();
    setSearchType(type);
  };

  return (
    <div style={{ ... props.style, "position": "relative" }}>
      <InputText icon={search} style={props.inputStyle}
        placeholder={props.placeholder || "Search"}
        value={query$()}
        showClearButton={true}
        onClick={async () => {
          if (!suggestionsVisible$()) {
            setSuggestionsVisible(true);
          }
        }}
        onFocusChanged={async (focus) => {
          if (focus) {
            setSearchHasFocus(true);
          } else {
            setSearchHasFocus(false);
          }
        }}
        onTextChanged={(v) => setQuery(v)}
        onSubmit={async (v) => await searchFor(v, searchType$())} />
        <Show when={suggestionsVisible$()}>
          <div class={styles.suggestionsContainer} onMouseDown={(e) => {
            e.stopPropagation();
            e.preventDefault();
          }} style={props.overlayStyle}>
            <div>Choose content type to search</div>
            <div style="display: flex; flex-direction: row; gap: 4px; margin-top: 16px;">
              <CustomButton icon={iconVideos} text='Media' style={{ ... buttonStyle, "background-color": searchType$() === ContentType.MEDIA ? "var(--grey-color-6)" : undefined }} iconStyle={buttonIconStyle} onMouseDown={(e) => changeSearchType(e, ContentType.MEDIA)} />
              <CustomButton icon={iconCreators} text='Creators' style={{ ... buttonStyle, "background-color": searchType$() === ContentType.CHANNEL ? "var(--grey-color-6)" : undefined }} iconStyle={buttonIconStyle} onMouseDown={(e) => changeSearchType(e, ContentType.CHANNEL)} />
              <CustomButton icon={iconPlaylist} text='Playlists' style={{ ... buttonStyle, "background-color": searchType$() === ContentType.PLAYLIST ? "var(--grey-color-6)" : undefined }} iconStyle={buttonIconStyle} onMouseDown={(e) => changeSearchType(e, ContentType.PLAYLIST)} />
            </div>
            <div style="width: 100%; background-color: var(--grey-color-6); height: 1px; margin-top: 20px; margin-bottom: 20px;"></div>
            <Show when={historical$()}>
              <div style="display: flex; flex-direction: row; margin-bottom: 12px; width: 100%;">
                <div>Your previous searches</div>
                <div style="flex-grow: 1"></div>

              </div>
            </Show>
            <Switch fallback={<div>No results found</div>}>
              <Match when={isLoadingSuggestions$()}>
                <div style="display: flex; flex-direction: row; width: 100%; align-items: center; justify-content: center;">
                  <LoaderSmall />
                </div>
              </Match>
              <Match when={suggestionItems$() && (suggestionItems$()?.length ?? 0) > 0}>
                <ScrollContainer style={{"max-height": "40vh", "width": "100%"}}>
                  <For each={suggestionItems$()}>{(item, i) => {
                    return (
                      <div class={styles.suggestionItem} onClick={async (e) => {
                        batch(() => {
                          setQuery(item);
                          setSuggestionsVisible(false);
                        });
                        await searchFor(item, searchType$());
                      }}>
                        <img src={iconSearch} style="width: 16px; height: 16px;" />
                        <div style="margin-left: 12px;">{item}</div>
                        <div style="flex-grow: 1"></div>
                        <img src={iconAddToQuery} style="width: 16px; height: 16px;" onMouseDown={(e) => {
                          setQuery(item);
                          e.preventDefault();
                          e.stopPropagation();
                        }} />
                        <Show when={historical$()}>
                          <img src={iconClose} style="width: 16px; height: 16px; margin-left: 16px;" onMouseDown={async (e) => {
                            e.preventDefault();
                            e.stopPropagation();
                            await SearchBackend.removePreviousSearch(item);
                            await getSuggestions(query$());
                          }} />
                        </Show>
                      </div>
                    );
                  }}</For>
                </ScrollContainer>
              </Match>
            </Switch>
          </div>
        </Show>
    </div>
  );
};

export default SearchBar;

/*
                <div style="margin-right: 12px; cursor: pointer;" onMouseDown={async (e) => {
                  e.preventDefault();
                  e.stopPropagation();
                  await SearchBackend.removeAllPreviousSearches();
                  await getSuggestions(query$());
                }}>Clear history</div>
*/