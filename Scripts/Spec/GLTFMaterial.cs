using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Siccity.GLTFUtility.Converters;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;
using Newtonsoft.Json.Linq;

namespace Siccity.GLTFUtility {
	// https://github.com/KhronosGroup/glTF/blob/master/specification/2.0/README.md#material
	[Preserve] public class GLTFMaterial {
#if UNITY_EDITOR
		public static Material defaultMaterial { get { return _defaultMaterial != null ? _defaultMaterial : _defaultMaterial = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat"); } }
		private static Material _defaultMaterial;
#else
		public static Material defaultMaterial { get { return null; } }
#endif

		public string name;
		public PbrMetalRoughness pbrMetallicRoughness;
		public TextureInfo normalTexture;
		public TextureInfo occlusionTexture;
		public TextureInfo emissiveTexture;
		[JsonConverter(typeof(ColorRGBConverter))] public Color emissiveFactor = Color.black;
		[JsonConverter(typeof(EnumConverter))] public AlphaMode alphaMode = AlphaMode.OPAQUE;
		public float alphaCutoff = 0.5f;
		public bool doubleSided = true;
		public Extensions extensions;
		public JObject extras;

		#region MRTKShaderProperties
		private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
		private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
		private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
		private static readonly int ModeId = Shader.PropertyToID("_Mode");
		private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
		private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
		private static readonly int MetallicGlossMapId = Shader.PropertyToID("_MetallicGlossMap");
		private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
		private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
		private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
		private static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
		private static readonly int ChannelMapId = Shader.PropertyToID("_ChannelMap");
		private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
		private static readonly int NormalMapId = Shader.PropertyToID("_NormalMap");
		private static readonly int NormalMapScaleId = Shader.PropertyToID("_NormalMapScale");
		private static readonly int CullModeId = Shader.PropertyToID("_CullMode");


		#endregion

		public class ImportResult {
			public Material material;
		}

		public IEnumerator CreateMaterial(GLTFTexture.ImportResult[] textures, ShaderSettings shaderSettings,
			Action<Material> onFinish)
		{
			Material mat = null;
			IEnumerator en = null;
			// Load metallic-roughness materials
			Shader mrtkShader = Shader.Find("Graphics Tools/Standard");

			if (mrtkShader != null)
			{
				Material material;
				if (shaderSettings.overrideMaterial != null)
				{
					material = new Material(shaderSettings.overrideMaterial)
					{
						name = string.IsNullOrEmpty(name) ? $"glTF Material " : name
					};
				}
				else
				{
					material = new Material(mrtkShader)
					{
						name = string.IsNullOrEmpty(name) ? $"glTF Material " : name
					};
				}
				
				material.EnableKeyword("_DIRECTIONAL_LIGHT");
				material.EnableKeyword("_SPECULAR_HIGHLIGHTS");
				if (pbrMetallicRoughness.baseColorTexture?.index >= 0)
				{
					en = textures[pbrMetallicRoughness.baseColorTexture.index].GetTextureCached(false, tex =>
					{
						if (tex != null)
						{
							material.mainTexture = tex;
						}
					});
					while (en.MoveNext())
					{
						yield return null;
					}
				}

				material.color = pbrMetallicRoughness.baseColorFactor;

				if (alphaMode == AlphaMode.MASK)
				{
					material.SetInt(SrcBlendId, (int)BlendMode.One);
					material.SetInt(DstBlendId, (int)BlendMode.Zero);
					material.SetInt(ZWriteId, 1);
					material.SetInt(ModeId, 3);
					material.SetOverrideTag("RenderType", "Cutout");
					material.EnableKeyword("_ALPHATEST_ON");
					material.DisableKeyword("_ALPHABLEND_ON");
					material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
					material.renderQueue = 2450;
				}
				else if (alphaMode == AlphaMode.BLEND)
				{
					material.SetInt(SrcBlendId, (int)BlendMode.One);
					material.SetInt(DstBlendId, (int)BlendMode.OneMinusSrcAlpha);
					material.SetInt(ZWriteId, 0);
					material.SetInt(ModeId, 3);
					material.SetOverrideTag("RenderType", "Transparency");
					material.DisableKeyword("_ALPHATEST_ON");
					material.DisableKeyword("_ALPHABLEND_ON");
					material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
					material.renderQueue = 3000;
				}

				if (emissiveTexture?.index >= 0 && material.HasProperty("_EmissionMap"))
				{
					material.EnableKeyword("_EMISSION");
					material.SetColor(EmissiveColorId, emissiveFactor);
				}

				if (pbrMetallicRoughness.metallicRoughnessTexture?.index >= 0)
				{
					Texture2D oclTexture = null;

					if (occlusionTexture != null && occlusionTexture.index >= 0)
					{
						en = TryGetTexture(textures, occlusionTexture, true, tex =>
						{
							if (tex != null)
							{
								oclTexture = tex;
							}
						});
						while (en.MoveNext())
						{
							yield return null;
						}
					}

					en = TryGetTexture(textures, pbrMetallicRoughness.metallicRoughnessTexture, true, tex =>
					{
						if (tex != null)
						{
							if (tex.isReadable)
							{
								var pixels = tex.GetPixels();
								Color[] occlusionPixels = null;
								if (oclTexture != null &&
								    oclTexture.isReadable)
								{
									occlusionPixels = oclTexture.GetPixels();
								}

								//if (gltfObject.UseBackgroundThread) await BackgroundThread;

								var pixelCache = new Color[pixels.Length];

								for (int c = 0; c < pixels.Length; c++)
								{
									pixelCache[c].r =
										pixels[c].b; // MRTK standard shader metallic value, glTF metallic value
									pixelCache[c].g =
										occlusionPixels?[c].r ??
										1.0f; // MRTK standard shader occlusion value, glTF occlusion value if available
									pixelCache[c].b = 0f; // MRTK standard shader emission value
									pixelCache[c].a =
										(1.0f - pixels[c]
											.g); // MRTK standard shader smoothness value, invert of glTF roughness value
								}

								//if (gltfObject.UseBackgroundThread) await Update;
								tex.SetPixels(pixelCache);
								tex.Apply();

								material.SetTexture(ChannelMapId, tex);
								material.EnableKeyword("_CHANNEL_MAP");
							}
							else
							{
								material.DisableKeyword("_CHANNEL_MAP");
							}
						}
					});
					while (en.MoveNext())
					{
						yield return null;
					}

					material.SetFloat(SmoothnessId, Mathf.Abs(pbrMetallicRoughness.roughnessFactor - 1f));
					material.SetFloat(MetallicId, (float)pbrMetallicRoughness.metallicFactor);
				}

				if (normalTexture?.index >= 0)
				{
					en = TryGetTexture(textures, normalTexture, true, tex =>
					{
						if (tex != null)
						{
							material.SetTexture(NormalMapId, tex);
							material.SetFloat(NormalMapScaleId, normalTexture.scale);
							material.EnableKeyword("_NORMAL_MAP");
						}
					});
					while (en.MoveNext())
					{
						yield return null;
					}
				}

				if (doubleSided)
				{
					material.SetFloat(CullModeId, (float)CullMode.Off);
				}

				material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
				onFinish(material);
			}
			else
			{
				if (pbrMetallicRoughness != null)
				{
					en = pbrMetallicRoughness.CreateMaterial(textures, alphaMode, shaderSettings, x => mat = x);
					while (en.MoveNext())
					{
						yield return null;
					}
				}
				// Load specular-glossiness materials
				else if (extensions != null && extensions.KHR_materials_pbrSpecularGlossiness != null)
				{
					en = extensions.KHR_materials_pbrSpecularGlossiness.CreateMaterial(textures, alphaMode,
						shaderSettings, x => mat = x);
					while (en.MoveNext())
					{
						yield return null;
					}

					;
				}
				// Load fallback material
				else
				{
					mat = new Material(Shader.Find("Standard"));
				}


				// Normal texture
				if (normalTexture != null)
				{
					en = TryGetTexture(textures, normalTexture, true, tex =>
					{
						if (tex != null)
						{
							mat.SetTexture("_BumpMap", tex);
							mat.EnableKeyword("_NORMALMAP");
							mat.SetFloat("_BumpScale", normalTexture.scale);
							if (normalTexture.extensions != null)
							{
								normalTexture.extensions.Apply(normalTexture, mat, "_BumpMap");
							}
						}
					});
					while (en.MoveNext())
					{
						yield return null;
					}
				}

				// Occlusion texture
				if (occlusionTexture != null)
				{
					en = TryGetTexture(textures, occlusionTexture, true, tex =>
					{
						if (tex != null)
						{
							mat.SetTexture("_OcclusionMap", tex);
							if (occlusionTexture.extensions != null)
							{
								occlusionTexture.extensions.Apply(occlusionTexture, mat, "_OcclusionMap");
							}
						}
					});
					while (en.MoveNext())
					{
						yield return null;
					}
				}

				// Emissive factor
				if (emissiveFactor != Color.black)
				{
					mat.SetColor("_EmissionColor", emissiveFactor);
					mat.EnableKeyword("_EMISSION");
				}

				// Emissive texture
				if (emissiveTexture != null)
				{
					en = TryGetTexture(textures, emissiveTexture, false, tex =>
					{
						if (tex != null)
						{
							mat.SetTexture("_EmissionMap", tex);
							mat.EnableKeyword("_EMISSION");
							if (emissiveTexture.extensions != null)
							{
								emissiveTexture.extensions.Apply(emissiveTexture, mat, "_EmissionMap");
							}
						}
					});
					while (en.MoveNext())
					{
						yield return null;
					}
				}

				if (alphaMode == AlphaMode.MASK)
				{
					mat.SetFloat("_AlphaCutoff", alphaCutoff);
				}

				mat.name = name;
				onFinish(mat);
			}
		}

		public static IEnumerator TryGetTexture(GLTFTexture.ImportResult[] textures, TextureInfo texture, bool linear, Action<Texture2D> onFinish, Action<float> onProgress = null) {
			if (texture == null || texture.index < 0) {
				if (onProgress != null) onProgress(1f);
				onFinish(null);
			}
			if (textures == null) {
				if (onProgress != null) onProgress(1f);
				onFinish(null);
			}
			if (textures.Length <= texture.index) {
				Debug.LogWarning("Attempted to get texture index " + texture.index + " when only " + textures.Length + " exist");
				if (onProgress != null) onProgress(1f);
				onFinish(null);
			}
			IEnumerator en = textures[texture.index].GetTextureCached(linear, onFinish, onProgress);
			while (en.MoveNext()) { yield return null; };
		}

		[Preserve] public class Extensions {
			public PbrSpecularGlossiness KHR_materials_pbrSpecularGlossiness = null;
		}

		// https://github.com/KhronosGroup/glTF/blob/master/specification/2.0/README.md#pbrmetallicroughness
		[Preserve] public class PbrMetalRoughness {
			[JsonConverter(typeof(ColorRGBAConverter))] public Color baseColorFactor = Color.white;
			public TextureInfo baseColorTexture;
			public float metallicFactor = 1f;
			public float roughnessFactor = 1f;
			public TextureInfo metallicRoughnessTexture;

			public IEnumerator CreateMaterial(GLTFTexture.ImportResult[] textures, AlphaMode alphaMode, ShaderSettings shaderSettings, Action<Material> onFinish) {
				// Shader
				Shader sh = null;
				if (alphaMode == AlphaMode.BLEND) sh = shaderSettings.MetallicBlend;
				else sh = shaderSettings.Metallic;

				// Material
				Material mat = new Material(sh);
				mat.color = baseColorFactor;
				mat.SetFloat("_Metallic", metallicFactor);
				mat.SetFloat("_Roughness", roughnessFactor);

				// Assign textures
				if (textures != null) {
					// Base color texture
					if (baseColorTexture != null && baseColorTexture.index >= 0) {
						if (textures.Length <= baseColorTexture.index) {
							Debug.LogWarning("Attempted to get basecolor texture index " + baseColorTexture.index + " when only " + textures.Length + " exist");
						} else {
							IEnumerator en = textures[baseColorTexture.index].GetTextureCached(false, tex => {
								if (tex != null) {
									mat.SetTexture("_MainTex", tex);
									if (baseColorTexture.extensions != null) {
										baseColorTexture.extensions.Apply(baseColorTexture, mat, "_MainTex");
									}
								}
							});
							while (en.MoveNext()) { yield return null; };
						}
					}
					// Metallic roughness texture
					if (metallicRoughnessTexture != null && metallicRoughnessTexture.index >= 0) {
						if (textures.Length <= metallicRoughnessTexture.index) {
							Debug.LogWarning("Attempted to get metallicRoughness texture index " + metallicRoughnessTexture.index + " when only " + textures.Length + " exist");
						} else {
							IEnumerator en = TryGetTexture(textures, metallicRoughnessTexture, true, tex => {
								if (tex != null) {
									mat.SetTexture("_MetallicGlossMap", tex);
									mat.EnableKeyword("_METALLICGLOSSMAP");
									if (metallicRoughnessTexture.extensions != null) {
										metallicRoughnessTexture.extensions.Apply(metallicRoughnessTexture, mat, "_MetallicGlossMap");
									}
								}
							});
							while (en.MoveNext()) { yield return null; };
						}
					}
				}

				// After the texture and color is extracted from the glTFObject
				if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mat.mainTexture);
				if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColorFactor);
				onFinish(mat);
			}
		}

		[Preserve] public class PbrSpecularGlossiness {
			/// <summary> The reflected diffuse factor of the material </summary>
			[JsonConverter(typeof(ColorRGBAConverter))] public Color diffuseFactor = Color.white;
			/// <summary> The diffuse texture </summary>
			public TextureInfo diffuseTexture;
			/// <summary> The reflected diffuse factor of the material </summary>
			[JsonConverter(typeof(ColorRGBConverter))] public Color specularFactor = Color.white;
			/// <summary> The glossiness or smoothness of the material </summary>
			public float glossinessFactor = 1f;
			/// <summary> The specular-glossiness texture </summary>
			public TextureInfo specularGlossinessTexture;

			public IEnumerator CreateMaterial(GLTFTexture.ImportResult[] textures, AlphaMode alphaMode, ShaderSettings shaderSettings, Action<Material> onFinish) {
				// Shader
				Shader sh = null;
				if (alphaMode == AlphaMode.BLEND) sh = shaderSettings.SpecularBlend;
				else sh = shaderSettings.Specular;

				// Material
				Material mat = new Material(sh);
				mat.color = diffuseFactor;
				mat.SetColor("_SpecColor", specularFactor);
				mat.SetFloat("_GlossyReflections", glossinessFactor);

				// Assign textures
				if (textures != null) {
					// Diffuse texture
					if (diffuseTexture != null) {
						if (textures.Length <= diffuseTexture.index) {
							Debug.LogWarning("Attempted to get diffuseTexture texture index " + diffuseTexture.index + " when only " + textures.Length + " exist");
						} else {
							IEnumerator en = textures[diffuseTexture.index].GetTextureCached(false, tex => {
								if (tex != null) {
									mat.SetTexture("_MainTex", tex);
									if (diffuseTexture.extensions != null) {
										diffuseTexture.extensions.Apply(diffuseTexture, mat, "_MainTex");
									}
								}
							});
							while (en.MoveNext()) { yield return null; };
						}
					}
					// Specular texture
					if (specularGlossinessTexture != null) {
						if (textures.Length <= specularGlossinessTexture.index) {
							Debug.LogWarning("Attempted to get specularGlossinessTexture texture index " + specularGlossinessTexture.index + " when only " + textures.Length + " exist");
						} else {
							mat.EnableKeyword("_SPECGLOSSMAP");
							IEnumerator en = textures[specularGlossinessTexture.index].GetTextureCached(false, tex => {
								if (tex != null) {
									mat.SetTexture("_SpecGlossMap", tex);
									mat.EnableKeyword("_SPECGLOSSMAP");
									if (specularGlossinessTexture.extensions != null) {
										specularGlossinessTexture.extensions.Apply(specularGlossinessTexture, mat, "_SpecGlossMap");
									}
								}
							});
							while (en.MoveNext()) { yield return null; };
						}
					}
				}
				onFinish(mat);
			}
		}

		// https://github.com/KhronosGroup/glTF/blob/master/specification/2.0/README.md#normaltextureinfo
		[Preserve] public class TextureInfo {
			[JsonProperty(Required = Required.Always)] public int index;
			public int texCoord = 0;
			public float scale = 1;
			public Extensions extensions;

			[Preserve] public class Extensions {
				public KHR_texture_transform KHR_texture_transform;

				public void Apply(GLTFMaterial.TextureInfo texInfo, Material material, string textureSamplerName) {
					// TODO: check if GLTFObject has extensionUsed/extensionRequired for these extensions

					if (KHR_texture_transform != null) {
						KHR_texture_transform.Apply(texInfo, material, textureSamplerName);
					}
				}
			}

			public interface IExtension {
				void Apply(GLTFMaterial.TextureInfo texInfo, Material material, string textureSamplerName);
			}
		}

		public class ImportTask : Importer.ImportTask<ImportResult[]> {
			private List<GLTFMaterial> materials;
			private GLTFTexture.ImportTask textureTask;
			private ImportSettings importSettings;

			public ImportTask(List<GLTFMaterial> materials, GLTFTexture.ImportTask textureTask, ImportSettings importSettings) : base(textureTask) {
				this.materials = materials;
				this.textureTask = textureTask;
				this.importSettings = importSettings;

				task = new Task(() => {
					if (materials == null) return;
					Result = new ImportResult[materials.Count];
				});
			}

			public override IEnumerator OnCoroutine(Action<float> onProgress = null) {
				// No materials
				if (materials == null) {
					if (onProgress != null) onProgress.Invoke(1f);
					IsCompleted = true;
					yield break;
				}

				for (int i = 0; i < Result.Length; i++) {
					Result[i] = new ImportResult();

					IEnumerator en = materials[i].CreateMaterial(textureTask.Result, importSettings.shaderOverrides, x => Result[i].material = x);
					while (en.MoveNext()) { yield return null; };

					if (Result[i].material.name == null) Result[i].material.name = "material" + i;
					if (onProgress != null) onProgress.Invoke((float) (i + 1) / (float) Result.Length);
					yield return null;
				}
				IsCompleted = true;
			}
		}
	}
}