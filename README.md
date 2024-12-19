
# Grayjay Desktop
Grayjay is a multi-platform media application that allows you to watch content from multiple platforms in a single application. Using an extendable plugin system developers can make new integrations with additional platforms. Plugins are cross-compatible between Android and Desktop.

FUTO is an organization dedicated to developing, both through in-house engineering and investment, technologies that frustrate centralization and industry consolidation.

For more elaborate showcase of features and downloads, check out the website.
Website: https://grayjay.app/desktop/

**NOTE for MacOS Users:** Our Apple signing/notarization is not entirely done yet, thus you have to run the following command once to run the application.
```
xattr -c ./Grayjay_osx-arm64.app

```
or
```
xattr -c ./Grayjay_osx-x64.app
```


### Home
Here you find the recommendations found on respective applications.

![Home](https://gitlab.futo.org/videostreaming/Grayjay.Desktop/-/raw/master/imgs/home.PNG)


### Sources
Here you install new source plugins, change which sources are used, or configure your source behavior.

![Sources](https://gitlab.futo.org/videostreaming/Grayjay.Desktop/-/raw/master/imgs/sources.PNG)

### Details
Here is an example of what the video player looks like, we support various views so that you can view the video how you like. By default we show a theater view that becomes smaller when reading comments, while not entirely hiding it.

|  |  |
|--|--|
| ![Details 1](https://gitlab.futo.org/videostreaming/Grayjay.Desktop/-/raw/master/imgs/detail1.PNG) | ![Details 2](https://gitlab.futo.org/videostreaming/Grayjay.Desktop/-/raw/master/imgs/detail2.PNG) |

### Downloads
Grayjay also supports downloads, allowing offline viewing of videos, as well as exporting them to files usable outside of Grayjay.

![Downloads](https://gitlab.futo.org/videostreaming/Grayjay.Desktop/-/raw/master/imgs/download.PNG)

### Channel
![Channels](https://gitlab.futo.org/videostreaming/Grayjay.Desktop/-/raw/master/imgs/channel.PNG)


### More..
Grayjay Desktop has way more features than this, but for that, check out the website or download it yourself!



## NixOS config

Below a NixOS configuration in case you like to use Grayjay on NixOS.
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

