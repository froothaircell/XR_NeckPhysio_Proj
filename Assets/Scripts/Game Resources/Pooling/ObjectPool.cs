using CoreResources.Singleton;
using GameResources.Gameplay;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GameResources.Pooling
{
    public class ObjectPool : DestroyableMonoSingleton<ObjectPool>
    {
        [SerializeField]
        private GameObject _pooledPrefab;
        [SerializeField, Tooltip("Sets the initialization count for the prefab pool. Only set this before application start")]
        private int _initializedCount = 45;

        private bool _poolInitialized = false;
        private bool _poolLocked = true; // Ensures the spawn function isn't called right after a clean call
        // private Vector3 _originalScale = Vector3.one;

        private List<PooledItem> _pool = new List<PooledItem>();
        private List<PooledItem> _spawnedItems = new List<PooledItem>();

        public override void OnInit()
        {
            InitializePool();
        }

        public override void OnDeInit()
        {
            CleanPool();
        }

        public void InitializePool()
        {
            if (_poolInitialized) return;

            for (int i = 0; i < _initializedCount; i++)
            {
                var obj = Instantiate(_pooledPrefab, transform.position, transform.rotation, transform);
                _pool.Add(obj.GetComponent<PooledItem>());
                _pool[i].InitializePooledItem(this);
            }

            // _originalScale = _pool.First().transform.localScale;
            _poolInitialized = true;
        }

        public void UnlockPool()
        {
            _poolLocked = false;
        }

        public void CleanPool()
        {
            _poolLocked = true;

            while (_spawnedItems.Count > 0)
            {
                _spawnedItems[0].ReturnToPool();
            }
        }

        public PooledItem SpawnItem(Vector3 position, Quaternion rotation, 
            Action<PooledItem> beforeSpawn = null, 
            Action<PooledItem> afterSpawn = null)
        {
            if (!_poolLocked && (_pool == null || _pool.Count == 0))
                return null;

            var item = _pool[0];
            _pool.RemoveAt(0);
            _spawnedItems.Add(item);

            beforeSpawn?.Invoke(item);

            // item.transform.localScale *= GameplayHandler.Instance.CachedProjectileScaleFactor;
            item.transform.parent = null;
            item.SpawnItem(position, rotation);

            afterSpawn?.Invoke(item);

            return item;
        }

        public void ReturnItemToPool(PooledItem item)
        {
            _spawnedItems.Remove(item);
            _pool.Add(item);
            item.transform.parent = transform;
            // item.transform.localScale = _originalScale;
            item.transform.localPosition = Vector3.zero;
        }

        public void ScaleProjectiles(float scaleFactor)
        {
            foreach (ProjectileController item in _pool)
            {
                item.UpdateScale(scaleFactor);
            }
        }
    }
}