using UnityEngine;

public class RoomWalls : MonoBehaviour
{
    void Start()
    {
        CreateWall(new Vector3(1.5563f, 1.1412f, 0.54f + 1.64f), new Vector3(5.58f, 1.87f, 0.1f)); // передняя
        CreateWall(new Vector3(1.5563f, 1.1412f, 0.54f - 1.64f), new Vector3(5.58f, 1.87f, 0.1f)); // задняя
        CreateWall(new Vector3(1.5563f + 2.79f, 1.1412f, 0.54f), new Vector3(0.1f, 1.87f, 3.28f)); // правая
        CreateWall(new Vector3(1.5563f - 2.79f, 1.1412f, 0.54f), new Vector3(0.1f, 1.87f, 3.28f)); // левая
        CreateWall(new Vector3(1.5563f, 0.2f, 0.54f), new Vector3(5.58f, 0.1f, 3.28f)); // пол
    }

    void CreateWall(Vector3 position, Vector3 size)
    {
        GameObject wall = new GameObject("Wall");
        wall.transform.position = position;
        wall.transform.parent = transform;
        BoxCollider col = wall.AddComponent<BoxCollider>();
        col.size = size;
    }
}