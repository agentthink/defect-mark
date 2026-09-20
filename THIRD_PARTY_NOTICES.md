# Third-Party Notices

MaskFlow is distributed under the GNU Affero General Public License v3.0. It uses third-party software and model assets that remain subject to their own licenses and terms.

| Component | Use in MaskFlow | Upstream license / terms |
|---|---|---|
| Meta SAM 3 model weights | Image segmentation model | Access-controlled terms published on [Hugging Face](https://huggingface.co/facebook/sam3). The weights are not included in this repository. |
| Ultralytics | SAM 3 inference integration | [AGPL-3.0](https://github.com/ultralytics/ultralytics/blob/main/LICENSE) or a separately obtained Ultralytics Enterprise license. |
| Ultralytics CLIP fork | Automatic category classification | See the [upstream repository](https://github.com/ultralytics/CLIP) for its license and notices. |
| PyTorch / TorchVision | Model runtime | See the [PyTorch license](https://github.com/pytorch/pytorch/blob/main/LICENSE). |
| FastAPI and Uvicorn | Python inference API | See their upstream repositories for license terms. |
| Vue and Vite | Web frontend | See their upstream repositories for license terms. |
| ASP.NET Core and .NET packages | Business API | See the corresponding Microsoft and NuGet package licenses. |

This file is informational and is not legal advice. When redistributing MaskFlow, review the exact versions in the lockfiles and dependency manifests and retain all notices required by their upstream licenses.
