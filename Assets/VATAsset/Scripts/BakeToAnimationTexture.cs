using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[RequireComponent(typeof(Animator))]
public class BakeToAnimationTexture : MonoBehaviour
{
    private Animator animator;
    private List<SkinnedMeshRenderer> smrs = new List<SkinnedMeshRenderer>();

    private string basePath = "VATExportData";
    private string absolutePath => "Assets/" + basePath;

    private string _texturePath = "";
    private string _materialPath = "";
    private string _meshPath = "";
    private string _prefabPath = "";
    

    [SerializeField] private Shader _shader;
    List<Material> _materials = new List<Material>();

    private void Start()
    {
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;

        if (!System.IO.Directory.Exists(absolutePath))
        {
            AssetDatabase.CreateFolder("Assets", basePath);
        }

        _prefabPath = AssetDatabase.CreateFolder(absolutePath, transform.name);
        _materialPath = AssetDatabase.CreateFolder(AssetDatabase.GUIDToAssetPath(_prefabPath), "Materials");
        _texturePath = AssetDatabase.CreateFolder(AssetDatabase.GUIDToAssetPath(_prefabPath), "Textures");
        _meshPath = AssetDatabase.CreateFolder(AssetDatabase.GUIDToAssetPath(_prefabPath), "Mesh");


        StartCoroutine("Init");
    }

    IEnumerator Init()
    {
        animator = GetComponent<Animator>();
        smrs.AddRange(GetComponentsInChildren<SkinnedMeshRenderer>());


        var clips = new List<AnimationClip>();
        clips.AddRange(animator.runtimeAnimatorController.animationClips);

        var vCount = 0;
        foreach (var smr in smrs)
        {
            vCount += smr.sharedMesh.vertices.Length;
        }

        //メモリ展開用２乗テクスチャサイズ補正、元モデルの頂点数が２の乗数だと、一番効率が良い変換が行える
        var dxTexSize = 2;
        while (vCount > dxTexSize)
        {
            dxTexSize *= 2;
        }

        vCount = dxTexSize;


        var bakeCount = vCount;
        var oneFlameTime = (1f / bakeCount);


        foreach (var clip in clips)
        {
            TextureCreateInit(clip.name + "_point", vCount, bakeCount, out var newPointTex, out var pBuffer);
            TextureCreateInit(clip.name + "_normal", vCount, bakeCount, out var newNormalTex, out var nBuffer);
            TextureCreateInit(clip.name + "_tangent", vCount, bakeCount, out var newTangentTex, out var tBuffer);

            animator.Play(clip.name, 0, 0);
            yield return new WaitForEndOfFrame();

            for (int i = 0; i < bakeCount; i++)
            {
                var currentTime = oneFlameTime * i;
                animator.ForceStateNormalizedTime(currentTime);

                yield return new WaitForEndOfFrame();

                ResetBakeCount();
                foreach (var smr in smrs)
                {
                    Mesh mesh = new Mesh();
                    smr.BakeMesh(mesh);
                    BakeCurrentAnimationForBuffer(i, mesh, ref pBuffer, ref nBuffer, ref tBuffer, newPointTex);
                }
            }


            SaveTextureDatas(newPointTex, pBuffer, newNormalTex, nBuffer, newTangentTex, tBuffer);

            //マテリアル
            var material = new Material(_shader);
            material.name = clip.name;
            material.SetTexture("_PointCache", newPointTex);
            material.SetTexture("_NormalCache", newNormalTex);
            material.SetTexture("_TangentCache", newTangentTex);

            _materials.Add(material);
            SaveMaterial(material, _materialPath);
        }


        //メッシュ
        var newObj =
            CreateMeshObject(smrs, out var newMesh, vCount, _materials.Count > 0 ? _materials[0] : null); //確認用にオブジェクト生成
        SaveMeshData(newMesh, _meshPath, gameObject.name);

        //プレファブ
        var controller = newObj.AddComponent<VATController>();
        controller.Init(_materials);
        PrefabUtility.CreatePrefab(Path.Combine(AssetDatabase.GUIDToAssetPath(_prefabPath), newObj.name + ".prefab").Replace("\\", "/"), newObj);


        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        
        //動作確認
        transform.gameObject.SetActive(false);
        newObj.AddComponent<AutoAnimationChange>();
    }

    private void SaveTextureDatas(Texture2D newTexture, Color[] pBuffer, Texture2D newNormal, Color[] nBuffer,
        Texture2D newTangent, Color[] tBuffer)
    {
        newTexture.SetPixels(pBuffer);
        newTexture.Apply(); //てくすちゃ完成！

        newNormal.SetPixels(nBuffer);
        newNormal.Apply(); //てくすちゃ完成！

        newTangent.SetPixels(tBuffer);
        newTangent.Apply(); //てくすちゃ完成！
        //保存
        SaveTexture(newTexture, _texturePath);
        SaveTexture(newNormal, _texturePath);
        SaveTexture(newTangent, _texturePath);
    }

    /// <summary>
    /// 統合メッシュオブジェクトの作成
    /// </summary>
    /// <param name="smrs"></param>
    /// <param name="mesh"></param>
    /// <param name="maxBuff"></param>
    /// <returns></returns>
    GameObject CreateMeshObject(List<SkinnedMeshRenderer> smrs, out Mesh mesh, int maxBuff,
        Material material = null)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Vector2> uvs_Cache = new List<Vector2>();


        Dictionary<List<int>, Material> triangles = new Dictionary<List<int>, Material>();


        foreach (var smr in smrs)
        {
            Mesh m = smr.sharedMesh;

            //サブメッシュ用情報リスト群作成
            List<Vector3> v = new List<Vector3>();
            v.AddRange(m.vertices);
            List<Vector3> n = new List<Vector3>();
            n.AddRange(m.normals);
            List<Vector2> u = new List<Vector2>();
            u.AddRange(m.uv);
            List<int> t = new List<int>();
            t.AddRange(m.triangles);

            //ポリ連結情報変換（インデックスを移動）
            for (int i = 0; i < t.Count; i++)
            {
                t[i] += vertices.Count;
            }

            //情報を確定（リストに追加）
            triangles.Add(t, smr.material);
            vertices.AddRange(v);

            Vector2[] zeroUV = new Vector2[v.Count];
            if (v.Count > u.Count)
            {
                uvs.AddRange(zeroUV);
            }
            else
            {
                uvs.AddRange(u);
            }

            uvs_Cache.AddRange(zeroUV);


            normals.AddRange(n);
        }

        for (int i = 0; i < uvs_Cache.Count; i++)
        {
            uvs_Cache[i] = new Vector2((float) i / (float) maxBuff, 0f);
        }


        //オブジェクト生成
        GameObject obj = new GameObject(transform.name + "_VAT");
        //メッシュ設定
        mesh = new Mesh();
        mesh.vertices = vertices.ToArray();
        mesh.normals = normals.ToArray();
        mesh.SetUVs(0, uvs.ToArray());
        mesh.SetUVs(3, uvs_Cache.ToArray());
        mesh.subMeshCount = triangles.Count;

        //サブメッシュ追加、サブメッシュ用マテリアルリスト作成
        List<Material> materials = new List<Material>();
        int materialNum = 0;
        foreach (var kv in triangles)
        {
            mesh.SetTriangles(kv.Key, materialNum);
            materials.Add(kv.Value);
            materialNum++;
        }
        //        mesh.triangles = triangles.ToArray();

        //メッシュフィルター設定
        var newMF = obj.AddComponent<MeshFilter>();
        newMF.sharedMesh = mesh;

        //マテリアル設定（サブメッシュ含）
        var mr = obj.AddComponent<MeshRenderer>();
        if (material != null)
        {
            mr.material = material;
        }
        else
        {
            mr.materials = materials.ToArray();
        }

        return obj;
    }


    public static void TextureCreateInit(string textureName, int sx, int sy, out Texture2D newTexture,
        out Color[] buffer)
    {
        //空の編集用テクスチャを生成
        int len = (int) (sx * sy);
        //テクスチャマネージャを生成
        newTexture = new Texture2D(sx, sy, TextureFormat.RGBAHalf, false);


        //テクスチャ塗りつぶし
        buffer = newTexture.GetPixels();
        // 塗りつぶす
        for (int x = 0; x < newTexture.width; x++)
        {
            for (int y = 0; y < newTexture.height; y++)
            {
                buffer.SetValue(Color.black, x + (newTexture.width * y));
            }
        }


        //マネーじゃのステータスを設定
        newTexture.name = textureName;
        newTexture.filterMode = FilterMode.Point;
        newTexture.wrapMode = TextureWrapMode.Repeat;
        newTexture.SetPixels(buffer);
        newTexture.Apply(); //てくすちゃ完成！
    }


    static public int currentBakeCount = 0;

    static void ResetBakeCount()
    {
        currentBakeCount = 0;
    }

    void BakeCurrentAnimationForBuffer(int flameNum, Mesh mesh, ref Color[] buffer, ref Color[] nBuffer,
        ref Color[] tBuffer, Texture2D tex)
    {
        var v = mesh.vertices;
        var n = mesh.normals;
        var t = mesh.tangents;


        for (int u = 0; u < v.Length; u++) //UVへの格納なので変数はu
        {
            //格納
            buffer[currentBakeCount + (tex.width * flameNum)] = new Color(v[u].x, v[u].y, v[u].z, 1f);
            nBuffer[currentBakeCount + (tex.width * flameNum)] = new Color(n[u].x, n[u].y, n[u].z, 1f);
            tBuffer[currentBakeCount + (tex.width * flameNum)] = new Color(t[u].x, t[u].y, t[u].z, t[u].w);
            currentBakeCount++;
        }
    }

    int CheckFileCount(string filePath, string typeCode)
    {
        int fileNum = 0;
        while (System.IO.File.Exists(filePath + fileNum.ToString() + typeCode))
        {
            fileNum++;
        }

        return fileNum;
    }

    static void SaveMaterial(Material material, string path)
    {

        AssetDatabase.CreateAsset(material,
            Path.Combine(AssetDatabase.GUIDToAssetPath(path), material.name + ".asset"));
    }

    static void SaveTexture(Texture2D tex, string path)
    {
        AssetDatabase.CreateAsset(tex, Path.Combine(AssetDatabase.GUIDToAssetPath(path), tex.name + ".asset"));
    }


    static void SaveMeshData(Mesh data, string path, string name)
    {
        AssetDatabase.CreateAsset(data, Path.Combine(AssetDatabase.GUIDToAssetPath(path), name + ".asset"));
    }
}