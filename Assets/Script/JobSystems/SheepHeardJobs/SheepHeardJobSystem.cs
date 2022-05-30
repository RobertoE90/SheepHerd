using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Mathematics;
using Unity.Collections;
using System;

public class SheepHeardJobSystem : SystemBase
{
    private const float SHEEP_MOVEMENT_SPEED = 1f;
    private const float SHEEP_TURN_SPEED = 1f;
    private EntityQuery _sheepsQuery;

    protected override void OnStartRunning()
    {
        _sheepsQuery = GetEntityQuery(new ComponentType[]
        {
            typeof(Translation),
            typeof(SheepFollowerComponent)
        });
    }

    protected override void OnUpdate()
    {
        var sheepPositions = _sheepsQuery.ToComponentDataArray<Translation>(Allocator.TempJob);

        UpdateMovementJob job = new UpdateMovementJob(
            GetComponentTypeHandle<Translation>(),
            GetComponentTypeHandle<Rotation>(),
            GetComponentTypeHandle<SheepFollowerComponent>(),
            sheepPositions,
            Time.DeltaTime);

        Dependency = job.ScheduleParallel(_sheepsQuery);
        Dependency.Complete();
        sheepPositions.Dispose();
    }
    /*
    protected override void OnUpdate()
    {   
        
        var deltaTime = Time.DeltaTime;

        int count = _sheepsQuery.CalculateEntityCount();
        var sheepPositions = _sheepsQuery.ToComponentDataArray<Translation>(Allocator.Persistent);

        var forward3D = new float3(0, 0, 1);
        Entities.ForEach((ref Translation translation, ref Rotation rotation, in SheepFollowerComponent follower) => 
        {
            var delta = (translation.Value - sheepPositions[follower.FollowIndex].Value).xz;
            var distance = math.distance(float2.zero, delta);
            delta = delta / distance;

            if (distance > 0.1f)
            {
                
                rotation.Value = math.slerp(
                    rotation.Value,
                    HorizontalLookAtRotation(delta * -1), 
                    SHEEP_TURN_SPEED * deltaTime);
            }
            translation.Value += math.mul(rotation.Value, forward3D) * SHEEP_MOVEMENT_SPEED * deltaTime;
           
            float4 HorizontalLookAtRotation(float2 normalizedVector)
            {
                var dot = Vector2.Dot(Vector2.up, normalizedVector);
                dot = (dot * -1f + 1f) * 0.5f;
                var radAngle = dot * math.PI;
                if (normalizedVector.x < 0f)
                    radAngle = (dot * -1 + 2) * math.PI;

                radAngle *= 0.5f;
                var cr = math.cos(radAngle);
                var sr = math.sin(radAngle);

                return new float4(0, sr, 0, cr);
            }

        }).Run();

        sheepPositions.Dispose();
    }
    */

    private struct UpdateMovementJob : IJobEntityBatch
    {
        public ComponentTypeHandle<Translation> translationType;
        public ComponentTypeHandle<Rotation> rotationType;
        [ReadOnly] public ComponentTypeHandle<SheepFollowerComponent> followerType;
        [ReadOnly] public NativeArray<Translation> heardTranslations;

        public float scaledDeltaTime;
        private float3 forward3D;

        public UpdateMovementJob(
            ComponentTypeHandle<Translation> t,
            ComponentTypeHandle<Rotation> r,
            ComponentTypeHandle<SheepFollowerComponent> ft,
            NativeArray<Translation> heardTranslations,
            float deltaTime)
        {
            translationType = t;
            rotationType = r;
            followerType = ft;
            scaledDeltaTime = deltaTime;
            this.heardTranslations = heardTranslations;
            forward3D = new float3(0, 0, 1);
        }

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            NativeArray<Translation> translations = batchInChunk.GetNativeArray(this.translationType);
            NativeArray<Rotation> rotations = batchInChunk.GetNativeArray(this.rotationType);
            NativeArray<SheepFollowerComponent> sheepFollowerComponents = batchInChunk.GetNativeArray(this.followerType);

            for (int i = 0; i < batchInChunk.Count; ++i)
            {
                Translation translation = translations[i];
                Rotation rotation = rotations[i];
                SheepFollowerComponent followerComponent = sheepFollowerComponents[i];

                var delta = (translation.Value - heardTranslations[followerComponent.FollowIndex].Value).xz;
                var distance = math.distance(float2.zero, delta);
                delta = delta / distance;

                if (distance > 0.1f)
                {

                    rotation.Value = math.slerp(
                        rotation.Value,
                        HorizontalLookAtRotation(delta * -1),
                        SHEEP_TURN_SPEED * scaledDeltaTime);
                }
                translation.Value += math.mul(rotation.Value, forward3D) * SHEEP_MOVEMENT_SPEED * scaledDeltaTime;
            }
        }

        private float4 HorizontalLookAtRotation(float2 normalizedVector)
        {
            var dot = Vector2.Dot(Vector2.up, normalizedVector);
            dot = (dot * -1f + 1f) * 0.5f;
            var radAngle = dot * math.PI;
            if (normalizedVector.x < 0f)
                radAngle = (dot * -1 + 2) * math.PI;

            radAngle *= 0.5f;
            var cr = math.cos(radAngle);
            var sr = math.sin(radAngle);

            return new float4(0, sr, 0, cr);
        }
    }
}
