local key = KEYS[1]
local nowTicks = tonumber(ARGV[1])
local permitLimit = tonumber(ARGV[2])
local windowTicks = tonumber(ARGV[3])
local burstCapacity = tonumber(ARGV[4])
local precisionTicks = tonumber(ARGV[5])
local requested = tonumber(ARGV[6])
local ttlSeconds = tonumber(ARGV[7])
local cooldownTicks = tonumber(ARGV[8])

local state = redis.call('HMGET', key, 'tokens', 'last_refill')
local tokens = tonumber(state[1])
local lastRefill = tonumber(state[2])

if tokens == nil or lastRefill == nil then
    tokens = burstCapacity
    lastRefill = nowTicks
end

local elapsed = nowTicks - lastRefill
if elapsed < 0 then
    elapsed = 0
end

if elapsed > 0 then
    local refilled = (elapsed * permitLimit) / windowTicks
    tokens = math.min(burstCapacity, tokens + refilled)
    lastRefill = nowTicks
end

requested = math.min(requested, burstCapacity)
local allowed = 0
local retryAfterTicks = 0
local used = 0

if tokens >= requested then
    allowed = 1
    tokens = tokens - requested
    used = requested
else
    local shortage = requested - tokens
    if shortage <= 0 then
        retryAfterTicks = precisionTicks
    else
        retryAfterTicks = math.ceil((shortage * windowTicks) / permitLimit)
        if retryAfterTicks < precisionTicks then
            retryAfterTicks = precisionTicks
        end
    end

    if cooldownTicks and cooldownTicks > retryAfterTicks then
        retryAfterTicks = cooldownTicks
    end
end

redis.call('HMSET', key,
    'tokens', tokens,
    'last_refill', nowTicks)

if ttlSeconds > 0 then
    redis.call('EXPIRE', key, ttlSeconds)
end

local tokensToFull = math.max(0, burstCapacity - tokens)
local resetAfterTicks
if tokensToFull <= 0 then
    resetAfterTicks = precisionTicks
else
    resetAfterTicks = math.ceil((tokensToFull * windowTicks) / permitLimit)
    if resetAfterTicks < precisionTicks then
        resetAfterTicks = precisionTicks
    end
end

if resetAfterTicks > windowTicks then
    resetAfterTicks = windowTicks
end

return {
    allowed,
    tostring(tokens),
    tostring(nowTicks),
    tostring(retryAfterTicks),
    tostring(resetAfterTicks),
    tostring(used)
}
