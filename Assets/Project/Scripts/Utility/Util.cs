namespace Project.Scripts
{
    public static class Util
    {
        public static float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393);
                h ^= (uint)(y * 668265263);
                h ^= (uint)(salt * 1442695041);

                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;

                return h / 4294967295f;
            }
        }
        
        public static float Hash01(int hash, int salt)
        {
            unchecked
            {
                uint h = (uint)(hash * 374761393);
                h ^= (uint)(hash * 668265263);
                h ^= (uint)(salt * 1442695041);

                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;

                return h / 4294967295f;
            }
        }
    }
}