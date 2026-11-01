# VidShrink Command Line

The long version of the *Command Line* section of the [README](../README.md).
[Türkçe](cli.tr.md)

The same package carries a headless CLI beside the app: `vidshrink` on Windows and Linux,
`vidshrink-cli` on macOS. It calls the same decision engine as the window, so the same
input gives the same ffmpeg arguments; a test holds the two against each other.

```bash
vidshrink kucult clip.mp4 --hedef 25MB              # shrink to a size
vidshrink kucult clip.mp4 --kalite 80 --kodek av1   # shrink to a quality score
vidshrink plan clip.mp4 --hedef 8MB --json          # plan and arguments only, no encode
```

## Options, English Aliases And Exit Codes

Options: `--kodek auto|h264|hevc|av1`, `--cikti <path>`, `--json`, `--olcumsuz` (skip the
probe encodes), `--vmaf` (measure the result when ffmpeg has libvmaf), `--hizli`. Progress
goes to stderr; size, duration, attempts and VMAF go to stdout. Help follows the system
language, Turkish or English. `--dil en` (`--lang en`) overrides it for one run; the
known codes are `en` and `tr`, and an unknown code is a usage error rather than a silent
fall back to English.

`--olcerek-ara` (`--search-by-measure`) works with `--kalite`: like the Advanced panel's
"Search by measuring" box it starts from the model's target, encodes short samples and
measures them, at most 4 rounds, and the plan takes the size that met the score. Off by
default; without libvmaf the model's target is kept. Rounds go to stderr (`"type":"search"`
with `--json`), the result to the plan's `search` field.

`--crf N` and `--on-ayar NAME` lock what the Advanced panel locks in the window: the
quality value (0-63) and the encoder preset. The preset name has to belong to the codec
the plan picks — `slow` for x264/x265, `p5` for NVENC, `8` for SVT-AV1 — and a name that
does not belong is dropped with a line in the plan reason, not an error.

`--modul N` (`--modulus N`) rounds the scaled edges down to a multiple of 2, 4, 8 or 16.
The default is 2, because an encoder cannot take an odd edge; a larger modulus is what old
hardware encoders want and crops the edge a little more. Any other number is a usage error.

An anamorphic source — a DVD whose stored pixels are not square — is de-anamorphosed
before scaling: the height is kept, the width becomes the width the frame is displayed
at, and the output carries square pixels. Nothing changes for a square-pixel source.

`--kes <start>-<end>` trims before the encode, so the target size is spent on the part you
keep: `--kes 10-40`, `--kes 0:10-0:40`, `--kes 1:02:03-1:02:04`, or `--kes 90-` to the end.

Either end may be a frame number instead of a clock: `--kes 300f-900f` is frames 300 to 900,
converted with the source frame rate. `--bolum 2` and `--bolum 2-4` build the same window from the
source's chapter marks; the two options cannot be given together.

`--yak N` (`--burn N`) burns the source's Nth subtitle into the picture. A text subtitle is
drawn by libass; an image subtitle (PGS, VOBSUB, DVB) is laid over the frame, scaled to its
width and bottom-aligned. MP4 cannot carry image subtitles, so without `--yak` they are
dropped and the plan says so, naming the option. The app does the same from its "Burn into
video" list, which offers text and image subtitles alike.

`--altyazi-tara` (`--subtitle-scan`) picks that track for you: the subtitle that only translates
the foreign-language parts of a film. A track flagged as forced wins; otherwise subtitle packets
are counted and the one track holding at most 10% of the fullest same-language track is burned
in. When the answer is not clear-cut nothing is burned and a line says why. It cannot be combined
with `--yak`. In the app the "Find foreign-language subtitle" button next to the "Burn into video"
list does the same and selects the track it finds.

Cover art is kept in MP4 and MKV output (png and jpeg in MKV, as an attachment). MOV cannot
carry it; the plan says so.

The long options below each answer to an English alias; the two spellings are the same
option, and a script may use either. `--crf` and `--json` and `--vmaf` are the exceptions: they have a
single spelling.

| Turkish | English |
|---|---|
| `--yardim` | `--help` |
| `--surum` | `--version` |
| `--hedef` | `--target` |
| `--kalite` | `--quality` |
| `--kodek` | `--codec` |
| `--on-ayar` | `--preset` |
| `--modul` | `--modulus` |
| `--bolum` | `--chapters` |
| `--cikti` | `--output` |
| `--kes` | `--cut` |
| `--aralik` | `--interval` |
| `--bir-kez` | `--once` |
| `--gunluk` | `--log` |
| `--tarama` | `--scan` |
| `--baslik` | `--title` |
| `--ana-icerik` | `--main-feature` |
| `--asgari-sure` | `--min-duration` |
| `--azami-sure` | `--max-duration` |
| `--suzgec` | `--filters` |
| `--kirp` | `--crop` |
| `--kirpma-kipi` | `--crop-mode` |
| `--profil` | `--profile` |
| `--profil-dosyasi` | `--preset-file` |
| `--ses-kodek` | `--audio-codec` |
| `--ses-normal` | `--loudnorm` |
| `--ses-temizle` | `--denoise` |
| `--ses-kazanc` | `--gain` |
| `--altyazi` | `--subtitle` |
| `--yan-altyazi` | `--sidecar-subtitles` |
| `--ses-dosyasi` | `--audio-file` |
| `--ses-degistir` | `--replace-audio` |
| `--yak` | `--burn` |
| `--yak-srt` | `--srt-burn` |
| `--yak-ass` | `--ssa-burn` |
| `--yak-yazi-tipi` | `--burn-font` |
| `--yak-boyut` | `--burn-size` |
| `--yak-kutu` | `--burn-box` |
| `--meta-yok` | `--no-metadata` |
| `--altyazi-dil` | `--subtitle-lang` |
| `--ilk-altyazi` | `--first-subtitle` |
| `--altyazi-tara` | `--subtitle-scan` |
| `--sabit-kare` | `--cfr` |
| `--tavan-kare` | `--pfr` |
| `--kare-hizi` | `--fps` |
| `--ses-hizi` | `--arate` |
| `--ses-drc` | `--drc` |
| `--profiller` | `--presets` |
| `--olcumsuz` | `--no-measure` |
| `--olcerek-ara` | `--search-by-measure` |
| `--hizli` | `--fast` |
| `--oncelik` | `--priority` |
| `--is-parcacigi` | `--threads` |
| `--dil` | `--lang` |
| `--kap` | `--container` |
| `--bit-hizi` | `--bitrate` |
| `--cozunurluk` | `--resolution` |
| `--ses-bit-hizi` | `--audio-bitrate` |
| `--disa-aktar` | `--export` |
| `--ek-arguman` | `--extra-args` |
| `--etiket-baslik` | `--tag-title` |
| `--etiket-sanatci` | `--tag-artist` |
| `--etiket-yil` | `--tag-year` |
| `--etiket-yorum` | `--tag-comment` |
| `--kapak` | `--cover` |
| `--onar` | `--repair` |
| `--bolum-ekle` | `--add-chapter` |
| `--bolumsuz` | `--no-chapters` |
| `--kip` | `--mode` |

Exit codes: `0` in band, `2` under the band (quality saturated, the smaller file kept), `3`
size ceiling exceeded (the smallest result is still written; JSON carries `output` and `overTarget: true`), `1` error, `64` wrong usage, `130` cancelled.

## Watch Folder

```bash
vidshrink izle ~/Gelen --cikti ~/Giden --hedef 25MB             # run until Ctrl+C
vidshrink izle ~/Gelen --cikti ~/Giden --hedef 25MB --bir-kez   # drain the folder, then exit
```

`izle` shrinks every video that lands in the folder. `--cikti` is the output folder and is
required; it cannot be the watched folder.

`--aralik <seconds>` sets the scan interval
(default, unmeasured): 2, `--bir-kez` exits once nothing is left to wait for, and the other `kucult`
options apply to each file. With `--json`, stdout is NDJSON: one compact JSON object per file.

A file is taken only when its size and modification time stay the same over two consecutive scan
intervals - the third scan takes it - and no writer holds it. If the source changes while it is being encoded, the output is
deleted and the file is picked up again once it settles.

Progress is kept in `.vidshrink-izle.json` inside the watched folder, by name and size; a
renamed or resized file counts as new. If that folder is read-only the state goes to the
output folder as `.vidshrink-izle-<hash>.json`, and failing that to the settings folder as
`izle-<hash>.json`. A file that failed is retried once on the next start. To process
everything again, delete the state file.

`<hash>` is the first 16 hex characters, lowercase, of the SHA-256 of the watched folder's
path. The path enters the hash under the same rule as the two comparisons below: upper-cased
first where the running system ignores case (Windows and macOS), taken as it stands on
Linux. So `/gelen` and `/Gelen` get one shared state file on Windows and macOS and two
separate ones on Linux, and the same folder always produces the same name.

Exactly two comparisons follow the rule of the running system: the watched folder against
the output folder, and a candidate against the output names this run has written. Those are
`Ordinal` on Linux and `OrdinalIgnoreCase` on Windows and macOS.

The macOS half of that rule assumes the default APFS volume, which is case-insensitive but
case-preserving. APFS can also be formatted case-sensitive, and on such a volume — or on a
case-sensitive external volume — the assumption does not hold.

Every other use of a file's name ignores case **on every platform, Linux included**: the
pending, retry and skip tables, the scan order, and the record of what has been processed.
So `Klip.mp4` and `klip.mp4` in one watched folder collide even where the filesystem keeps
them apart as two separate files.

A collision is resolved in the scan, not left to rot. Of the colliding names the ordinally
smallest wins and is shrunk as usual; the others are skipped with one warning line each,
`Skipped: klip.mp4 — its name collides with Klip.mp4 (letter case only). Rename one of
them.`, written once per file and not repeated on later scans. The winner is the same on
every scan, so the run is deterministic. Rename the skipped file and the watcher picks it
up as a new file.

Exit codes: `0` finished, `4` `--bir-kez` finished but at least one file failed or was
skipped for a name collision, `1` error, `64` wrong usage, `130` stopped with Ctrl+C. After
`--bir-kez` a summary line reports how many files were skipped.

## Convert

`donustur` (`convert`) is the Convert tab without the window: the same argument builder
(`ConversionArguments`), the same codec and container sets, the same output name
(`<name>_converted.<ext>` next to the source). With the same settings the CLI and the tab
write the same ffmpeg command; a test holds the two against each other.

```bash
vidshrink donustur clip.mov --kap mp4 --kodek hevc --crf 24
vidshrink donustur clip.mp4 -o clip.webm --kodek vp9 --bit-hizi 2000
vidshrink donustur clip.mp4 --kap gif --cozunurluk 480 --kare-hizi 15
vidshrink donustur clip.mkv --kap mp3 --ses-bit-hizi 192 --json
```

Options: `--kap <container>` (mp4, mkv, webm, mov, avi, gif, mp3, m4a, wav, flac, webp,
avif, mxf, png, jpg, opus, ogg, aiff, ac3; without it the extension of `--cikti` decides,
otherwise mp4), `--kodek <codec>` (h264, hevc, vp9, av1, prores, dnxhr, `copy`, or an encoder
name such as `libx264`, `h264_nvenc`, `prores_ks:hq`), `--crf <n>` (inside the codec's own
range) or `--bit-hizi <kbit/s>` (not both), `--cozunurluk source|<height>|<W>x<H>`,
`--kare-hizi source|<fps>`, `--ses-kodek <codec>` (aac, opus, mp3, pcm, flac, ac3, eac3,
`copy`, `none`), `--ses-bit-hizi <kbit/s>`, `--kes <range>`, `--meta-yok`, `--cikti <path>`,
`--ek-arguman "<options>"` (extra ffmpeg options written just before the output path; quotes
keep spaces, repeating the flag appends; `-i`, `-y`, `-n`, `-f`, `-progress`, `-`, `--` and a
bare value that would become a second output are refused with `64`), `--json`. A combination the tab would refuse (an audio codec the container cannot carry, an
odd size) is refused with the same reason and exit code `64`; a container whose encoder the
local ffmpeg lacks gives `1`.

Tags, cover, Repair and chapters follow the tab's Tags and Chapters drop-downs:
`--etiket-baslik`, `--etiket-sanatci`, `--etiket-yil`, `--etiket-yorum` write the container's
title, artist, date and comment (an empty value writes no key; `--meta-yok` does not remove them);
`--kapak <png|jpg>` adds a cover (MKV attachment, MP4 cover track with video copy only; a missing
file gives `1`). `--onar` is the tab's Repair preset: corrupt packets dropped, missing timestamps
rebuilt, output starting at zero; without `--kap`, `--kodek`, `--ses-kodek` it remuxes video and
audio into MKV. `--bolum-ekle <time>=<name>` (repeatable, `90=Main` or `1:30=Main`) replaces the
source chapters with this list, counted from the source start and shifted by `--kes`;
`--bolumsuz` writes no chapters. Both need mp4, mkv or mov and cannot be given together (`64`).

```bash
vidshrink donustur broken.ts --onar
vidshrink donustur talk.mkv --etiket-baslik "Talk" --bolum-ekle 0=Intro --bolum-ekle 12:30=Questions
```

The CLI does not carry the tab's GIF-from-GIF size target, its audio track picker or its
external audio file.

## Export A Project

`teslim` (`export`) exports a project the editor saved (`.vsproj.json`) without the window.
The timeline is built from the project (`EditProject.ToJob`): every clip, text, music track,
audio track choice and added source; planning and running are the editor's own export path
(`EditExportRunner`). The CLI's `--kes` cuts a single range; a multi-part cut is repeated with
this command from a project saved in the editor.

```bash
vidshrink teslim cut.vsproj.json
vidshrink teslim cut.vsproj.json --cikti final.mp4 --kip tam --json
```

Options: `--kip hizli|akilli|tam` (`--mode fast|smart|full`; without it the mode saved in the
project, smart when that is not known), `--cikti <path>` (default
`<name>-duzenlenmis.<ext>` beside the first source), `--meta-yok`, `--json`. Text, effects,
music or more than one source turn the mode to full as they do in the editor; stdout names
the mode applied.

When a source's size or write time changed after the project was saved, or the source is
missing, the export does not start and the exit code is `1`; an unreadable project, an empty
timeline and a reversed clip over the memory limit give `1` too. An output path that is a
source or the project itself gives `64`. Writing clips to separate files and a queued batch
export are not part of this command.

## Progress As NDJSON

With `--json`, stderr progress is NDJSON too, one compact object per line, for `kucult`,
`plan` and `donustur`. stdout keeps the result object as before. In `izle` the per-file
progress lines follow the same format; the watcher's own event lines stay text.

```json
{"type":"stage","stage":"probe"}
{"type":"progress","percent":42,"fraction":0.4213,"stage":"pass 1/2","remainingSeconds":12.5}
```

`stage` lines name a step (`probe`, `crop`, `measure`, `calibrate` and so on);
`progress` lines repeat only when the percent or the stage changes, and `remainingSeconds`
is `null` while it is unknown.

## Exporting Presets

`profiller --disa-aktar <file>` writes your own presets to a JSON file that the app's
"Import" reads (`PresetLibrary.Export`). `--profil <id or name>` (repeatable) picks some of
them; without it all are written. Built-in presets ship with every copy and cannot be
exported; asking for one gives `64`, as does an unknown name.

```bash
vidshrink profiller --disa-aktar profiles.json
vidshrink profiller --disa-aktar discord.json --profil "Discord clip"
```

## Shell Completion

`tamamlama <shell>` (`completion`) prints a completion script for `bash`, `zsh`, `fish` or
`powershell` (`pwsh`). It completes commands, options and the values of options with a
closed set (codecs, containers, profiles, languages).

```bash
source <(vidshrink tamamlama bash)                              # ~/.bashrc
source <(vidshrink tamamlama zsh)                               # ~/.zshrc
vidshrink tamamlama fish | source                               # ~/.config/fish/config.fish
vidshrink tamamlama powershell | Out-String | Invoke-Expression # $PROFILE
```
