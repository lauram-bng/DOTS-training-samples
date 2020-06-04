using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


[ExecuteAlways]
public class GridCreationSystem : SystemBase
{
    public NativeArray<CellInfo> Cells { get; private set; }

    private EntityCommandBufferSystem m_commandBuffer;

    protected override void OnCreate()
    {
        m_commandBuffer = World.GetExistingSystem<EndInitializationEntityCommandBufferSystem>();

        base.OnCreate();
    }

    protected override void OnDestroy()
    {
        if (Cells.IsCreated)
        {
            Cells.Dispose();
        }

        base.OnDestroy();
    }

    protected override void OnUpdate()
    {
        var constantData = ConstantData.Instance;

        if(!Cells.IsCreated && constantData != null)
        {
            // Ugly Simon's code, if it works, don't change it!
            unsafe
            {
                Unity.Physics.BoxCollider* boardCollider = (Unity.Physics.BoxCollider*)GetSingleton<Unity.Physics.PhysicsCollider>().ColliderPtr;
                var geometry = boardCollider->Geometry;
                geometry.Size = new float3(constantData.BoardDimensions.x, 1, constantData.BoardDimensions.y);
                geometry.Center = new float3(constantData.BoardDimensions.x / 2, -1, constantData.BoardDimensions.y / 2);
                boardCollider->Geometry = geometry;
            }

            Cells = new NativeArray<CellInfo>(constantData.BoardDimensions.x * constantData.BoardDimensions.y, Allocator.Persistent);

            var cellsarray = Cells;

            int width = constantData.BoardDimensions.x;
            int height = constantData.BoardDimensions.y;
            int bottomRight = (width * height) - 1;

            Job.WithCode(() => 
            {
                int bottomLeft = width * (height - 1);

                cellsarray[0] = cellsarray[0].SetTravelDirections(GridDirection.NORTH | GridDirection.EAST);

                cellsarray[width - 1] = cellsarray[width - 1].SetTravelDirections(GridDirection.NORTH | GridDirection.WEST);
                cellsarray[bottomLeft] = cellsarray[bottomLeft].SetTravelDirections(GridDirection.SOUTH | GridDirection.EAST);
                cellsarray[bottomRight] = cellsarray[(width * height) - 1].SetTravelDirections(GridDirection.SOUTH | GridDirection.WEST);

                GridDirection fromNorth = GridDirection.SOUTH | GridDirection.EAST | GridDirection.WEST;
                GridDirection fromSouth = GridDirection.NORTH | GridDirection.EAST | GridDirection.WEST;
                GridDirection fromWest = GridDirection.NORTH | GridDirection.SOUTH | GridDirection.EAST;
                GridDirection fromEast = GridDirection.NORTH | GridDirection.SOUTH | GridDirection.WEST;

                for (int i = 1; i < (width - 1); i++)
                {
                    cellsarray[i] = cellsarray[i].SetTravelDirections(fromSouth);
                    cellsarray[bottomLeft + i] = cellsarray[bottomLeft + i].SetTravelDirections(fromNorth);
                }

                for (int i = 1; i < (height - 1); i++)
                {
                    cellsarray[width * i] = cellsarray[width * i].SetTravelDirections(fromWest);
                    cellsarray[(width * (i + 1)) - 1] = cellsarray[(width * (i + 1)) - 1].SetTravelDirections(fromEast);
                }

                cellsarray[width + 1] = cellsarray[width + 1].SetIsHole();

                //position bases
                int xOffset = (int)(width * 0.333f);
                int yOffset = (int)(height * 0.333f);

                int baseIndex = (yOffset * width) + xOffset; 

                cellsarray[baseIndex] = cellsarray[baseIndex].SetBasePlayerId(0);

                baseIndex = (yOffset * 2 * width) + xOffset * 2;

                cellsarray[baseIndex] = cellsarray[baseIndex].SetBasePlayerId(1);

                baseIndex = (yOffset * 2 * width) + xOffset;

                cellsarray[baseIndex] = cellsarray[baseIndex].SetBasePlayerId(2);

                baseIndex = (yOffset * width) + xOffset * 2;

                cellsarray[baseIndex] = cellsarray[baseIndex].SetBasePlayerId(3);

            }).Schedule();

            var ecb = m_commandBuffer.CreateCommandBuffer();
            var cellSize = constantData.CellSize;

            Entities.ForEach((in PrefabReferenceComponent prefabs) => {

                //create cells and bases
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int index = (width * y) + x;
                        if (!cellsarray[index].IsHole())
                        {
                            Entity prefab = ((x + y) % 2) == 1 ? prefabs.CellOddPrefab : prefabs.CellPrefab;
                            var entity = ecb.Instantiate(prefab);

                            if (entity != Entity.Null)
                            {
                                ecb.SetComponent(entity, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(x, y), cellSize) });
                            }
                        }

                        if (cellsarray[index].IsBase())
                        {
                            int playerId = cellsarray[index].GetBasePlayerId();
                            Entity basePrefab = Entity.Null;

                            switch (playerId)
                            {
                                case 0:
                                    basePrefab = prefabs.BasePrefab0;
                                    break;

                                case 1:
                                    basePrefab = prefabs.BasePrefab1;
                                    break;

                                case 2:
                                    basePrefab = prefabs.BasePrefab2;
                                    break;

                                case 3:
                                    basePrefab = prefabs.BasePrefab3;
                                    break;
                            }

                            if(basePrefab != Entity.Null)
                            {
                                var entity = ecb.Instantiate(basePrefab);

                                if (entity != Entity.Null)
                                {
                                    ecb.SetComponent(entity, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(x, y), cellSize) });
                                }
                            }
                        }
                    }

                    var wall = ecb.Instantiate(prefabs.WallPrefab);

                    if(wall != Entity.Null)
                    {
                        ecb.SetComponent(wall, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(x, 0), cellSize) });
                        ecb.SetComponent(wall, new Rotation2D { Value = Utility.DirectionToAngle(GridDirection.SOUTH) });
                    }

                    wall = ecb.Instantiate(prefabs.WallPrefab);

                    if (wall != Entity.Null)
                    {
                        ecb.SetComponent(wall, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(x, width - 1), cellSize) });
                        ecb.SetComponent(wall, new Rotation2D { Value = Utility.DirectionToAngle(GridDirection.NORTH) });
                    }
                }

                //create spawners
                var spawner = ecb.Instantiate(prefabs.MouseSpawnerPrefab);
                if(spawner != Entity.Null)
                {
                    ecb.SetComponent(spawner, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(0, 0), cellSize) });
                    ecb.SetComponent(spawner, new Direction2D { Value = GridDirection.NORTH });
                }

                spawner = ecb.Instantiate(prefabs.MouseSpawnerPrefab);
                if (spawner != Entity.Null)
                {
                    ecb.SetComponent(spawner, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(width - 1, height - 1), cellSize) });
                    ecb.SetComponent(spawner, new Direction2D { Value = GridDirection.SOUTH });
                }

                spawner = ecb.Instantiate(prefabs.CatSpawnerPrefab);
                if (spawner != Entity.Null)
                {
                    ecb.SetComponent(spawner, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(width - 1, 0), cellSize) });
                    ecb.SetComponent(spawner, new Direction2D { Value = GridDirection.WEST });
                }

                spawner = ecb.Instantiate(prefabs.CatSpawnerPrefab);
                if (spawner != Entity.Null)
                {
                    ecb.SetComponent(spawner, new Position2D { Value = Utility.GridCoordinatesToWorldPos(new int2(0, height - 1), cellSize) });
                    ecb.SetComponent(spawner, new Direction2D { Value = GridDirection.EAST });
                }

            }).Schedule();

            m_commandBuffer.AddJobHandleForProducer(Dependency);

            /*for (int i = 0; i <= bottomRight; i++)
            {
                var directions = i +" can travel: ";
                if (cellsarray[i].CanTravel(GridDirection.NORTH)) directions += "N";
                if (cellsarray[i].CanTravel(GridDirection.EAST)) directions += "E";
                if (cellsarray[i].CanTravel(GridDirection.SOUTH)) directions += "S";
                if (cellsarray[i].CanTravel(GridDirection.WEST)) directions += "W";

                Debug.Log(directions);
                Debug.Log(i + " is hole: " + cellsarray[i].IsHole());
            }

            Debug.Log("cell infos created"); */
        }
    }
}