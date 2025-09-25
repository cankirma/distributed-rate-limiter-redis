local key = KEYS[1]
local nowTicks = tonumber(ARGV[1])
local permitLimit = tonumber(ARGV[2])
local windowTicks = tonumber(ARGV[3])
local burstCapacity = tonumber(ARGV[4])
local precisionTicks = tonumber(ARGV[5])
local requested = tonumber(ARGV[6])
local ttlSeconds = tonumber(ARGV[7])
local cooldownTicks = tonumber(ARGV[8])

local state = redis.call('HMGET', key, 'water_level', 'last_drip')
local waterLevel = tonumber(state[1])
local lastDrip = tonumber(state[2])

if waterLevel == nil or lastDrip == nil then
    waterLevel = 0
    lastDrip = nowTicks
end

local elapsed = nowTicks - lastDrip
if elapsed < 0 then
    elapsed = 0
end

if elapsed > 0 then
    local leaked = (elapsed * permitLimit) / windowTicks
    waterLevel = math.max(0, waterLevel - leaked)
    lastDrip = nowTicks
end

requested = math.min(requested, burstCapacity)
local allowed = 0
local retryAfterTicks = 0
local used = 0

local newLevel = waterLevel + requested
if newLevel <= burstCapacity then
    allowed = 1
    waterLevel = newLevel
    used = requested
else
    local overflow = newLevel - burstCapacity
    if overflow <= 0 then
        retryAfterTicks = precisionTicks
    else
        retryAfterTicks = math.ceil((overflow * windowTicks) / permitLimit)
        if retryAfterTicks < precisionTicks then
            retryAfterTicks = precisionTicks
        end
    end

    if cooldownTicks and cooldownTicks > retryAfterTicks then
        retryAfterTicks = cooldownTicks
    end
end

redis.call('HMSET', key,
    'water_level', waterLevel,
    'last_drip', nowTicks)

if ttlSeconds > 0 then
    redis.call('EXPIRE', key, ttlSeconds)
end

local remaining = math.max(0, burstCapacity - waterLevel)
local resetAfterTicks
if waterLevel <= 0 then
    resetAfterTicks = precisionTicks
else
    resetAfterTicks = math.ceil((waterLevel * windowTicks) / permitLimit)
    if resetAfterTicks < precisionTicks then
        resetAfterTicks = precisionTicks
    end
end

if resetAfterTicks > windowTicks then
    resetAfterTicks = windowTicks
end

return {
    allowed,
    tostring(waterLevel),
    tostring(nowTicks),
    tostring(retryAfterTicks),
    tostring(resetAfterTicks),
    tostring(used)
}
