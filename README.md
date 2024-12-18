#Grayjay Desktop

Website: https://grayjay.app/desktop/

README still todo





# NixOS config

```
(pkgs.buildFHSUserEnv {
  name = "fhs";
  targetPkgs = _: with pkgs; [
    libz
    icu
    openssl # For updater

    xorg.libX11
    xorg.libXcomposite
    xorg.libXdamage
    xorg.libXext
    xorg.libXfixes
    xorg.libXrandr
    xorg.libxcb

    gtk3
    glib
    nss
    nspr
    dbus
    atk
    cups
    libdrm
    expat
    libxkbcommon
    pango
    cairo
    udev
    alsa-lib
    mesa
    libGL
    libsecret
  ];
}).env
```
