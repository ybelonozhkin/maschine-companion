# Third-party notices

The companion's own source is licensed under [MIT](LICENSE).

## Windows MIDI Services

The Live integration uses the Microsoft Windows MIDI Services App SDK,
Windows.Devices.Midi2 0.99.92-preview.11, under the MIT License.

Copyright (c) Microsoft Corporation.

[Source release](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-11)
and [full license](LICENSES/microsoft-midi-MIT.txt).

## C++/WinRT

The native MIDI bridge is built with Microsoft C++/WinRT support under the MIT
License. The build uses the Windows SDK header version 2.0.250303.1.

Copyright (c) Microsoft Corporation.

[Source](https://github.com/microsoft/cppwinrt)
and [full license](LICENSES/cppwinrt-MIT.txt).

## Controller protocol references

The controller implementation was informed by these community protocol references:

- [r00tman/maschine-mikro-mk3-driver](https://github.com/r00tman/maschine-mikro-mk3-driver/tree/30f9b6013f3559dc1774d3ddb82affabd376692a): OLED packets, LED reports, palette and button layout. Copyright (c) 2025 r00tman. [MIT License](LICENSES/r00tman-MIT.txt).
- [OpenAV Ctlra](https://github.com/openAVproductions/openAV-Ctlra/tree/8b6cf95712c0e67c92b93fa270c3ee094f870d28): native LED report behavior. Copyright (c) 2017 OpenAV Productions, Harry van Haaren and Michał Ciemięga. [BSD 3-Clause License](LICENSES/openav-BSD-3-Clause.txt).

These projects are acknowledged as protocol references; their libraries are not
bundled with the companion.
