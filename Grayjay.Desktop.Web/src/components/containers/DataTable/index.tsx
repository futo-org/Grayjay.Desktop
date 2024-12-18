import { Component, createSignal, onCleanup, onMount, For, JSX, createMemo, Match, Switch, Show } from "solid-js";
import styles from './index.module.css';
import Checkbox from "../../basics/inputs/Checkbox";
import CheckboxFlex from "../../basics/inputs/CheckboxFlex";

export interface DataTableProps {
    style?: JSX.CSSProperties;
    rowStyle?: JSX.CSSProperties;
    columnInfo: any[],
    data: any[],
    selectable?: boolean,
    onSelectionChanged?: (selected: any[]) => void
};
export interface DataTableColumn {
    name: string,
    resolve: (item: any)=>any,
    type?: string,
    style?: JSX.CSSProperties,
    onClick?: (item: any)=>any
};


const DataTable: Component<DataTableProps> = (props) => {
    let selected:any[] = [];
    let [selected$, setSelected] = createSignal(selected);

    function toggleSelected(item: any) {
        const existingIndex = selected.indexOf(item);
        if(existingIndex >= 0)
            selected.splice(existingIndex, 1);
        else
            selected.push(item);
        setSelected([...selected]);
        if(props.onSelectionChanged)
            props.onSelectionChanged!(selected);
    }
    function toggleAll() {
        const clear = selected.length > 0;
        for(let item of props.data) {
            const index = selected.indexOf(item);
            if(clear && index >= 0)
                selected.splice(index, 1);
            else if(!clear && index < 0)
                selected.push(item);
        }
        setSelected([...selected]);
        if(props.onSelectionChanged)
            props.onSelectionChanged!(selected);
    }
    return (
        <div class={styles.containerScroll} style={props.style}>
            <div>
                <table>
                    <tbody>
                        <tr class={styles.header}>
                            <Show when={props.selectable}>
                                <th style="text-align: center">
                                    <Checkbox value={props.data.length == selected$().length} onChecked={()=>toggleAll()} />
                                </th>
                            </Show>
                            <For each={props.columnInfo}>{ column =>
                                <th>{column.name}</th>
                            }</For>
                        </tr>
                        <For each={props.data}>{ row =>
                            <tr style={props.rowStyle}>
                                <Show when={props.selectable}>
                                    <td style="text-align: center">
                                        <Checkbox value={selected$().indexOf(row) >= 0} onChecked={()=>toggleSelected(row)} />
                                    </td>
                                </Show>
                                <For each={props.columnInfo}>{ column =>
                                    <td style={column.style}>
                                        <Switch fallback={
                                            <span classList={{[styles.isClickable]: !!column.onClick}} onClick={()=>{(column.onClick) ? column.onClick(row) : null}}>
                                                {(column.resolve(row))}
                                            </span>
                                        }>
                                            <Match when={column.type == "image"}>
                                                <div class={styles.tableImage} classList={{[styles.isClickable]: !!column.onClick}} onClick={()=>{(column.onClick) ? column.onClick(row) : null}}>
                                                    <img src={column.resolve(row)} />
                                                </div>
                                            </Match>
                                        </Switch>
                                    </td>
                                }</For>
                            </tr>
                        }</For>
                    </tbody>
                </table>
            </div>
        </div>
    );
};

export default DataTable;
