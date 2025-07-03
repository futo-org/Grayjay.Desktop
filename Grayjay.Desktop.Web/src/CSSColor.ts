export class CSSColor {
    // -- RGBA channels stored 0–1 --
    private _r: number;
    private _g: number;
    private _b: number;
    private _a: number;

    // -- HSLA storage & lazy‐recompute flag --
    private _h: number = 0;
    private _s: number = 0;
    private _l: number = 0;
    private _hslDirty: boolean = true;

    constructor(r: number, g: number, b: number, a: number = 1) {
        // enforce [0,1] on construction
        if (![r, g, b, a].every(ch => ch >= 0 && ch <= 1)) {
            throw new Error("RGBA channels must be in [0,1]");
        }
        this._r = r; this._g = g; this._b = b; this._a = a;
    }

    // -- Float views 0–1 --
    get r(): number { return this._r; }
    set r(v: number) { this._r = clamp01(v); this._hslDirty = true; }
    get g(): number { return this._g; }
    set g(v: number) { this._g = clamp01(v); this._hslDirty = true; }
    get b(): number { return this._b; }
    set b(v: number) { this._b = clamp01(v); this._hslDirty = true; }
    get a(): number { return this._a; }
    set a(v: number) { this._a = clamp01(v); }

    // -- Int views 0–255 --
    get red(): number { return Math.round(this._r * 255); }
    set red(v: number) { this.r = clampInt(v) / 255; }
    get green(): number { return Math.round(this._g * 255); }
    set green(v: number){ this.g = clampInt(v) / 255; }
    get blue(): number { return Math.round(this._b * 255); }
    set blue(v: number) { this.b = clampInt(v) / 255; }
    get alpha(): number { return Math.round(this._a * 255); }
    set alpha(v: number){ this.a = clampInt(v) / 255; }

    // -- HSL views (lazy‐computed) --
    get hue(): number {
        this.computeHslIfNeeded();
        return this._h;
    }
    set hue(v: number) {
        this.setHsl(v, this.saturation, this.lightness);
    }

    get saturation(): number {
        this.computeHslIfNeeded();
        return this._s;
    }
    set saturation(v: number) {
        this.setHsl(this.hue, v, this.lightness);
    }

    get lightness(): number {
        this.computeHslIfNeeded();
        return this._l;
    }
    set lightness(v: number) {
        this.setHsl(this.hue, this.saturation, v);
    }

    private computeHslIfNeeded(): void {
        if (!this._hslDirty) return;
        const { _r: r, _g: g, _b: b } = this;
        const max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
        const l = (max + min) / 2;
        const s = d === 0 ? 0 : d / (1 - Math.abs(2 * l - 1));
        let hPrime: number;
        if (d === 0) {
            hPrime = 0;
        } else if (max === r) {
            hPrime = ((g - b) / d) % 6;
        } else if (max === g) {
            hPrime = ( (b - r) / d ) + 2;
        } else {
            hPrime = ( (r - g) / d ) + 4;
        }
        let h = hPrime * 60;
        if (h < 0) h += 360;

        this._h = h;
        this._s = s;
        this._l = l;
        this._hslDirty = false;
    }

    /** Set all three HSL channels at once. Hue [0...360), S/L [0...1]. */
    setHsl(h: number, s: number, l: number): void {
        // normalize hue into [0,360)
        const hh = ((h % 360) + 360) % 360;
        const c = (1 - Math.abs(2 * l - 1)) * s;
        const x = c * (1 - Math.abs((hh / 60) % 2 - 1));
        const m = l - c / 2;

        let rp;
        let gp;
        let bp;
        if (hh < 60) rp = c, gp = x, bp = 0;
        else if (hh < 120) rp = x, gp = c, bp = 0;
        else if (hh < 180) rp = 0, gp = c, bp = x;
        else if (hh < 240) rp = 0, gp = x, bp = c;
        else if (hh < 300) rp = x, gp = 0, bp = c;
        else rp = c, gp = 0, bp = x;

        this._r = rp + m;
        this._g = gp + m;
        this._b = bp + m;
        this._h = hh;
        this._s = s;
        this._l = l;
        this._hslDirty = false;
    }

    /** Return 0xRRGGBBAA */
    toRgbaInt(): number {
        const ri = (this.red & 0xFF) << 24;
        const gi = (this.green & 0xFF) << 16;
        const bi = (this.blue & 0xFF) << 8;
        const ai = (this.alpha & 0xFF);
        return (ri | gi | bi | ai) >>> 0;
    }

    /** Return 0xAARRGGBB */
    toArgbInt(): number {
        const ai = (this.alpha & 0xFF) << 24;
        const ri = (this.red & 0xFF) << 16;
        const gi = (this.green & 0xFF) << 8;
        const bi = (this.blue & 0xFF);
        return (ai | ri | gi | bi) >>> 0;
    }

    // — Convenience modifiers (chainable) —
    lighten(f: number): this { this.lightness = clamp01(this.lightness + f); return this; }
    darken(f: number): this { this.lightness = clamp01(this.lightness - f); return this; }
    saturate(f: number): this { this.saturation = clamp01(this.saturation + f); return this; }
    desaturate(f: number): this { this.saturation = clamp01(this.saturation - f); return this; }
    rotateHue(d: number): this { this.hue = (this.hue + d) % 360; return this; }

    // ——— Static factories & parsers ———

    /** Create from Android 0xAARRGGBB */
    static fromArgb(color: number): CSSColor {
        const a = ((color >>> 24) & 0xFF) / 255;
        const r = ((color >>> 16) & 0xFF) / 255;
        const g = ((color >>> 8) & 0xFF) / 255;
        const b = (color & 0xFF) / 255;
        return new CSSColor(r, g, b, a);
    }

    /** Create from Android 0xRRGGBBAA */
    static fromRgba(color: number): CSSColor {
        const r = ((color >>> 24) & 0xFF) / 255;
        const g = ((color >>> 16) & 0xFF) / 255;
        const b = ((color >>> 8) & 0xFF) / 255;
        const a = (color & 0xFF) / 255;
        return new CSSColor(r, g, b, a);
    }

    private static readonly NAMED_HEX: Record<string,string> = {
        aliceblue: "F0F8FF", antiquewhite: "FAEBD7", aqua: "00FFFF", aquamarine: "7FFFD4", azure: "F0FFFF", beige: "F5F5DC", 
        bisque: "FFE4C4", black: "000000", blanchedalmond: "FFEBCD", blue: "0000FF", blueviolet: "8A2BE2", brown: "A52A2A", 
        burlywood: "DEB887", cadetblue: "5F9EA0", chartreuse: "7FFF00", chocolate: "D2691E", coral: "FF7F50", cornflowerblue: "6495ED", 
        cornsilk: "FFF8DC", crimson: "DC143C", cyan: "00FFFF", darkblue: "00008B", darkcyan: "008B8B", darkgoldenrod: "B8860B", 
        darkgray: "A9A9A9", darkgreen: "006400", darkgrey: "A9A9A9", darkkhaki: "BDB76B", darkmagenta: "8B008B", darkolivegreen: "556B2F", 
        darkorange: "FF8C00", darkorchid: "9932CC", darkred: "8B0000", darksalmon: "E9967A", darkseagreen: "8FBC8F", 
        darkslateblue: "483D8B", darkslategray: "2F4F4F", darkslategrey: "2F4F4F", darkturquoise: "00CED1", darkviolet: "9400D3", 
        deeppink: "FF1493", deepskyblue: "00BFFF", dimgray: "696969", dimgrey: "696969", dodgerblue: "1E90FF", firebrick: "B22222", 
        floralwhite: "FFFAF0", forestgreen: "228B22", fuchsia: "FF00FF", gainsboro: "DCDCDC", ghostwhite: "F8F8FF", gold: "FFD700", 
        goldenrod: "DAA520", gray: "808080", green: "008000", greenyellow: "ADFF2F", grey: "808080", honeydew: "F0FFF0", hotpink: "FF69B4", 
        indianred: "CD5C5C", indigo: "4B0082", ivory: "FFFFF0", khaki: "F0E68C", lavender: "E6E6FA", lavenderblush: "FFF0F5", 
        lawngreen: "7CFC00", lemonchiffon: "FFFACD", lightblue: "ADD8E6", lightcoral: "F08080", lightcyan: "E0FFFF", 
        lightgoldenrodyellow: "FAFAD2", lightgray: "D3D3D3", lightgreen: "90EE90", lightgrey: "D3D3D3", lightpink: "FFB6C1", 
        lightsalmon: "FFA07A", lightseagreen: "20B2AA", lightskyblue: "87CEFA", lightslategray: "778899", lightslategrey: "778899", 
        lightsteelblue: "B0C4DE", lightyellow: "FFFFE0", lime: "00FF00", limegreen: "32CD32", linen: "FAF0E6", magenta: "FF00FF", 
        maroon: "800000", mediumaquamarine: "66CDAA", mediumblue: "0000CD", mediumorchid: "BA55D3", mediumpurple: "9370DB", 
        mediumseagreen: "3CB371", mediumslateblue: "7B68EE", mediumspringgreen: "00FA9A", mediumturquoise: "48D1CC", mediumvioletred: "C71585", 
        midnightblue: "191970", mintcream: "F5FFFA", mistyrose: "FFE4E1", moccasin: "FFE4B5", navajowhite: "FFDEAD", navy: "000080", 
        oldlace: "FDF5E6", olive: "808000", olivedrab: "6B8E23", orange: "FFA500", orangered: "FF4500", orchid: "DA70D6", 
        palegoldenrod: "EEE8AA", palegreen: "98FB98", paleturquoise: "AFEEEE", palevioletred: "DB7093", papayawhip: "FFEFD5", 
        peachpuff: "FFDAB9", peru: "CD853F", pink: "FFC0CB", plum: "DDA0DD", powderblue: "B0E0E6", purple: "800080", rebeccapurple: "663399", 
        red: "FF0000", rosybrown: "BC8F8F", royalblue: "4169E1", saddlebrown: "8B4513", salmon: "FA8072", sandybrown: "F4A460", 
        seagreen: "2E8B57", seashell: "FFF5EE", sienna: "A0522D", silver: "C0C0C0", skyblue: "87CEEB", slateblue: "6A5ACD", 
        slategray: "708090", slategrey: "708090", snow: "FFFAFA", springgreen: "00FF7F", steelblue: "4682B4", tan: "D2B48C", 
        teal: "008080", thistle: "D8BFD8", tomato: "FF6347", turquoise: "40E0D0", violet: "EE82EE", wheat: "F5DEB3", white: "FFFFFF", 
        whitesmoke: "F5F5F5", yellow: "FFFF00", yellowgreen: "9ACD32"
    };

    private static readonly HEX_REGEX = /^#([0-9a-fA-F]{3,8})$/;
    private static readonly RGB_REGEX = /^rgba?\(([^)]+)\)$/i;
    private static readonly HSL_REGEX = /^hsla?\(([^)]+)\)$/i;

    /** Parse any CSS color string (named, #hex, rgb(a), hsl(a)). */
    static parseColor(str: string): CSSColor {
        const s = str.trim();
        const lower = s.toLowerCase();

        // named
        if (lower === "transparent") {
            return new CSSColor(0,0,0,0);
        }
        if (this.NAMED_HEX[lower]) {
            return this.parseHexPart(this.NAMED_HEX[lower]);
        }

        // hex
        let m = s.match(this.HEX_REGEX);
        if (m) {
            return this.parseHexPart(m[1]);
        }

        // rgb/rgba
        m = s.match(this.RGB_REGEX);
        if (m) {
            return this.parseRgbParts(m[1].split(",").map(p => p.trim()));
        }

        // hsl/hsla
        m = s.match(this.HSL_REGEX);
        if (m) {
            return this.parseHslParts(m[1].split(",").map(p => p.trim()));
        }

        throw new Error(`Cannot parse color: "${s}"`);
    }

    private static parseHexPart(p: string): CSSColor {
        // expand shorthand to RRGGBBAA
        let hex = "";
        if (p.length === 3) hex = p.split("").map(c => c + c).join("") + "FF";
        else if (p.length === 4) hex = p.split("").map(c => c + c).join("");
        else if (p.length === 6) hex = p + "FF";
        else if (p.length === 8) hex = p;
        else throw new Error(`Invalid hex color: #${p}`);

        const parsed = parseInt(hex, 16) >>> 0;
        // parsed = 0xRRGGBBAA
        const aa = (parsed & 0xFF) << 24;
        const rgb = (parsed >>> 8) & 0x00FFFFFF;
        return CSSColor.fromArgb(aa | rgb);
    }

    private static parseRgbParts(parts: string[]): CSSColor {
        if (parts.length !== 3 && parts.length !== 4) {
            throw new Error("rgb/rgba needs 3 or 4 parts");
        }
        const toChannel = (ch: string) => {
            if (ch.endsWith("%")) {
                return clamp01(parseFloat(ch) / 100);
            } else {
                const v = Math.min(255, Math.max(0, parseFloat(ch)));
                return v / 255;
            }
        };
        const toAlpha = (a: string) =>
            a.endsWith("%")
                ? clamp01(parseFloat(a) / 100)
                : clamp01(parseFloat(a));
        const r = toChannel(parts[0]),
                    g = toChannel(parts[1]),
                    b = toChannel(parts[2]),
                    a = parts[3] ? toAlpha(parts[3]) : 1;
        return new CSSColor(r, g, b, a);
    }

    private static parseHslParts(parts: string[]): CSSColor {
        if (parts.length !== 3 && parts.length !== 4) {
            throw new Error("hsl/hsla needs 3 or 4 parts");
        }
        const toHue = (h: string) => {
            if (h.endsWith("deg")) return parseFloat(h);
            if (h.endsWith("grad")) return parseFloat(h) * 0.9;
            if (h.endsWith("rad")) return parseFloat(h) * (180 / Math.PI);
            if (h.endsWith("turn")) return parseFloat(h) * 360;
            return parseFloat(h);
        };
        const toPct = (p: string) => clamp01(parseFloat(p.replace("%","")) / 100);
        const toAlpha = (a: string) =>
            a.endsWith("%")
                ? clamp01(parseFloat(a.replace("%","")) / 100)
                : clamp01(parseFloat(a));
        const h = toHue(parts[0]),
                    s = toPct(parts[1]),
                    l = toPct(parts[2]),
                    a = parts[3] ? toAlpha(parts[3]) : 1;
        const col = new CSSColor(0,0,0,a);
        col.setHsl(h, s, l);
        return col;
    }
}

// ——— Helpers ———
function clamp01(x: number): number {
    return x < 0 ? 0 : x > 1 ? 1 : x;
}
function clampInt(x: number): number {
    return Math.min(255, Math.max(0, Math.floor(x)));
}
