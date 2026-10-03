# Third-party notices

Dyncamelo distributes the following third-party components. They remain under
their own licenses, reproduced below as required by their terms.

## Nodify

WPF node editor library — https://github.com/miroiu/nodify

> MIT License
>
> Copyright (c) 2020 Miroiu Emanuel
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

## Newtonsoft.Json (Json.NET)

JSON framework for .NET — https://github.com/JamesNK/Newtonsoft.Json

> MIT License
>
> Copyright (c) 2007 James Newton-King
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

## Microsoft Automatic Graph Layout (MSAGL)

Layered graph layout behind Arrange — https://github.com/microsoft/automatic-graph-layout
(NuGet package `AutomaticGraphLayout`, shipped as `AutomaticGraphLayout.dll`)

> MIT License
>
> Copyright (c) Microsoft Corporation. All rights reserved.
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

## Wiki build tools (MkDocs, Material for MkDocs, pymdown-extensions, mkdocs-glightbox)

Used only to build the wiki with `tools/build_wiki.py` (versions in `tools/wiki/requirements.txt`); they are not part of the
Dyncamelo install. The wiki site that the build writes (it is not committed; CI keeps it as an artifact) contains parts of
them, all MIT-licensed: the Material for MkDocs theme (styles, scripts, templates), the lunr.js search script it ships, the
GLightbox picture viewer that mkdocs-glightbox ships and the iframe-worker shim (`tools/wiki/overrides/assets/javascripts/`).
The icons inside the pages are from Material Design Icons (Pictogrammers Free License) and Simple Icons (CC0 1.0).
MkDocs itself (BSD 2-Clause, Copyright 2014-present Tom Christie) only runs the build and is not in the site.
https://squidfunk.github.io/mkdocs-material/ · https://github.com/facelessuser/pymdown-extensions · https://github.com/blueswen/mkdocs-glightbox · https://github.com/mkdocs/mkdocs

> MIT License
>
> Copyright (c) 2016-2025 Martin Donath (Material for MkDocs, iframe-worker)
> Copyright (c) 2014-2025 Isaac Muse (pymdown-extensions)
> Copyright (c) 2022 Blueswen (mkdocs-glightbox)
> Copyright (C) 2020 Oliver Nightingale (lunr.js)
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.

## Autodesk Navisworks API

Dyncamelo compiles against the Autodesk Navisworks 2024 API reference
assemblies. No Autodesk binaries are redistributed with Dyncamelo — the
user's licensed Navisworks installation supplies them at run time.
