-- Module cho Nakama: ghép 2 người chơi vào cùng một room rồi phát ticket.
-- Hỗ trợ cả room cụ thể (arena:1) và tự động cấp room từ pool (arena:1 .. arena:4).
--
-- Nakama nạp file này như một script (không có InitModule); RPC đăng ký khi nạp.
-- Chạy trong Nakama, mount từ deploy/nakama -> /nakama/data.
--
-- RPC cho client (Bearer token):
--   find_match  { "room": "auto" }     -> { status = "waiting", room = "auto" }
--                                        { status = "matched", room = "arena:1", ticket = "...", exp }
--   poll_match  { "room": "auto" }     -> như trên
-- RPC cho backend (http_key), không lộ ra client:
--   verify_ticket { "ticket": "...", "room": "arena:1", "user_id": "..." }
--     -> { ok = true, room = "...", exp = <unix> }

local nk = require("nakama")

local SYSTEM = "00000000-0000-0000-0000-000000000000"
local QUEUE = "match_queue"
local RESULT = "match_result"
local TICKET = "match_ticket"
local ROOMSTAT = "room_stat"
local TICKET_TTL = 120
local QUEUE_TTL = 30

local ROOM_POOL = { "arena:1", "arena:2", "arena:3", "arena:4" }
local POOL_COUNTER_KEY = "pool:counter"

local function decode(payload)
    if payload == nil or payload == "" then
        return {}
    end
    local ok, value = pcall(nk.json_decode, payload)
    if not ok then
        error({ "payload must be JSON", 3 })
    end
    return value
end

local function respond(value)
    return nk.json_encode(value)
end

local function backend_only(ctx)
    -- Gọi bằng http_key thì ctx.user_id rỗng; người chơi (Bearer) bị từ chối.
    if ctx.user_id ~= nil and ctx.user_id ~= "" then
        error({ "only the backend may call this RPC", 7 })
    end
end

local function read_one(collection, key)
    local records = nk.storage_read({ { collection = collection, key = key, user_id = SYSTEM } })
    if records == nil or #records == 0 then
        return nil
    end
    return records[1]
end

local function write_one(collection, key, value, version)
    local record = {
        collection = collection,
        key = key,
        user_id = SYSTEM,
        value = value,
        permission_read = 1,
        permission_write = 1,
    }

    if version ~= nil and version ~= "" then
        record.version = version
    end

    nk.storage_write({ record })
end

-- pool: danh sách room thực sự có pod (backend gửi kèm); không có thì dùng ROOM_POOL cố định.
local function next_pool_room(pool)
    local rooms = ROOM_POOL
    if type(pool) == "table" and #pool > 0 then
        rooms = pool
    end
    for _ = 1, 5 do
        local record = read_one(QUEUE, POOL_COUNTER_KEY)
        local cur = 0
        local version = "*"
        if record and record.value and record.value.index then
            cur = record.value.index
            version = record.version or ""
        end
        local next_idx = (cur % #rooms) + 1
        local ok = pcall(write_one, QUEUE, POOL_COUNTER_KEY, { index = next_idx }, version)
        if ok then
            return rooms[next_idx]
        end
    end
    return rooms[1]
end

local function ticket_for(assigned_room, queue_key, first, second)
    local ticket = nk.uuid_v4()
    local exp = os.time() + TICKET_TTL

    write_one(TICKET, ticket, { room = assigned_room, users = { first, second }, exp = exp }, "")

    local result = { status = "matched", room = assigned_room, ticket = ticket, exp = exp }

    -- Trạng thái room để list_rooms hiển thị
    write_one(ROOMSTAT, assigned_room, { users = { first, second }, exp = exp }, "")

    -- Lưu theo assigned_room
    write_one(RESULT, assigned_room .. ":" .. first, result, "")
    write_one(RESULT, assigned_room .. ":" .. second, result, "")

    -- Lưu theo queue_key (nếu là auto pool)
    if queue_key ~= assigned_room then
        write_one(RESULT, queue_key .. ":" .. first, result, "")
        write_one(RESULT, queue_key .. ":" .. second, result, "")
    end

    -- Lưu theo user_id để fallback tra cứu nhanh
    write_one(RESULT, "user:" .. first, result, "")
    write_one(RESULT, "user:" .. second, result, "")

    return result
end

local function is_auto_room(room)
    return room == nil or room == "" or room == "auto" or room == "pool" or room == "arena:pool"
end

local function find_match(ctx, payload)
    local data = decode(payload)
    local requested_room = data.room
    local is_auto = is_auto_room(requested_room)
    local queue_key = is_auto and "pool:auto" or requested_room
    local user = ctx.user_id
    local stamp = os.time()

    -- Bắt đầu một lượt ghép mới: bỏ kết quả cũ của người này để poll_match không trả về trận đã xong.
    local stale = read_one(RESULT, queue_key .. ":" .. user)
    if stale and stale.value and stale.value.ticket then
        local fresh = read_one(RESULT, "user:" .. user)
        local keys = { { collection = RESULT, key = queue_key .. ":" .. user, user_id = SYSTEM } }
        if fresh and fresh.value and fresh.value.ticket == stale.value.ticket then
            keys[#keys + 1] = { collection = RESULT, key = "user:" .. user, user_id = SYSTEM }
        end
        nk.storage_delete(keys)
    end

    for _ = 1, 5 do
        local record = read_one(QUEUE, queue_key)
        local waiting = nil

        if record and record.value and record.value.user_id and record.value.user_id ~= "" then
            if stamp - (record.value.since or 0) <= QUEUE_TTL then
                waiting = record.value.user_id
            end
        end

        -- Chưa có bản ghi: version "*" chỉ ghi khi chưa tồn tại. Với version rỗng Nakama không kiểm tra gì,
        -- nên hai người xin cùng lúc cùng thấy hàng chờ trống và ghi đè lên nhau, không ai được ghép.
        local version = record and record.version or "*"

        if waiting == user then
            return respond({ status = "waiting", room = is_auto and "auto" or requested_room })
        end

        if waiting then
            local cleared, clearError = pcall(write_one, QUEUE, queue_key, { user_id = "", since = 0 }, version)
            if cleared then
                local assigned_room = is_auto and next_pool_room(data.pool) or requested_room
                local result = ticket_for(assigned_room, queue_key, waiting, user)
                nk.logger_info("match: " .. waiting .. " + " .. user .. " -> " .. assigned_room .. " ticket " .. result.ticket)
                return respond(result)
            end

            nk.logger_info("find_match: failed to clear queue: " .. tostring(clearError))
        else
            local queued, queueError = pcall(write_one, QUEUE, queue_key, { user_id = user, since = stamp }, version)
            if queued then
                return respond({ status = "waiting", room = is_auto and "auto" or requested_room })
            end

            nk.logger_info("find_match: failed to write queue: " .. tostring(queueError))
        end
    end

    error({ "match_queue is busy, try again", 14 })
end

local function poll_match(ctx, payload)
    local data = decode(payload)
    local requested_room = data.room
    local is_auto = is_auto_room(requested_room)
    local user = ctx.user_id
    local queue_key = is_auto and "pool:auto" or requested_room

    -- Chỉ lấy kết quả của ĐÚNG hàng chờ này. Trước đây có fallback sang "user:<id>" nên người vừa ghép xong ở phòng cũ
    -- (còn hạn 120 giây) xin phòng khác lại nhận nhầm kết quả cũ.
    local record = read_one(RESULT, queue_key .. ":" .. user)

    if record and record.value then
        if record.value.exp and record.value.exp < os.time() then
            nk.storage_delete({
                { collection = RESULT, key = queue_key .. ":" .. user, user_id = SYSTEM },
                { collection = RESULT, key = "user:" .. user, user_id = SYSTEM }
            })
            return respond({ status = "waiting", room = is_auto and "auto" or requested_room })
        end

        return respond(record.value)
    end

    return respond({ status = "waiting", room = is_auto and "auto" or requested_room })
end

local function verify_ticket(ctx, payload)
    backend_only(ctx)
    local data = decode(payload)

    if data.ticket == nil or data.user_id == nil then
        return respond({ ok = false, message = "ticket and user_id are required" })
    end

    local record = read_one(TICKET, data.ticket)
    if record == nil then
        return respond({ ok = false, message = "ticket does not exist" })
    end

    local value = record.value
    if value.exp == nil or value.exp < os.time() then
        return respond({ ok = false, message = "ticket has expired" })
    end

    -- Nếu data.room là auto hoặc rỗng thì bỏ qua check room
    if data.room and not is_auto_room(data.room) and value.room ~= data.room then
        return respond({ ok = false, message = "ticket belongs to another room" })
    end

    local member = false
    if value.users then
        for _, uid in ipairs(value.users) do
            if uid == data.user_id then
                member = true
            end
        end
    end

    if not member then
        return respond({ ok = false, message = "ticket does not belong to this user" })
    end

    return respond({ ok = true, room = value.room, exp = value.exp })
end

local function reset_match(ctx, payload)
    backend_only(ctx)
    local data = decode(payload)
    local requested_room = data.room
    local queue_key = is_auto_room(requested_room) and "pool:auto" or (requested_room or "arena:1")
    nk.storage_delete({ { collection = QUEUE, key = queue_key, user_id = SYSTEM } })
    return respond({ ok = true })
end

local function waiting_in(queue_key)
    local record = read_one(QUEUE, queue_key)
    if record and record.value and record.value.user_id and record.value.user_id ~= "" then
        if os.time() - (record.value.since or 0) <= QUEUE_TTL then
            return true
        end
    end
    return false
end

-- Cho backend (http_key): trạng thái các room trong pool để lobby hiển thị.
-- players = số người đang chờ hoặc đã được ghép (chưa phải số kết nối thật tại pod).
local function list_rooms(ctx, payload)
    backend_only(ctx)
    local now = os.time()
    local rooms = {}

    local auto_players, auto_state = 0, "open"
    if waiting_in("pool:auto") then
        auto_players, auto_state = 1, "waiting"
    end
    rooms[#rooms + 1] = { room = "auto", players = auto_players, capacity = 2, state = auto_state }

    for _, room in ipairs(ROOM_POOL) do
        local players, state = 0, "open"
        local stat = read_one(ROOMSTAT, room)
        if stat and stat.value and stat.value.exp and stat.value.exp > now then
            players, state = 2, "full"
        elseif waiting_in(room) then
            players, state = 1, "waiting"
        end
        rooms[#rooms + 1] = { room = room, players = players, capacity = 2, state = state }
    end

    return respond({ rooms = rooms })
end

-- Kho key-value cho tài khoản / hồ sơ / bạn bè của backend (giá trị là chuỗi JSON do backend tự quản).
local function kv_get(ctx, payload)
    backend_only(ctx)
    local data = decode(payload)
    if data.collection == nil or data.key == nil then
        error({ "collection and key are required", 3 })
    end
    local record = read_one(data.collection, data.key)
    if record == nil then
        return respond({ found = false })
    end
    return respond({ found = true, value = record.value.v or "", version = record.version })
end

-- version: "" = ghi đè, "*" = chỉ khi chưa tồn tại, khác = chỉ khi version khớp.
local function kv_put(ctx, payload)
    backend_only(ctx)
    local data = decode(payload)
    if data.collection == nil or data.key == nil then
        error({ "collection and key are required", 3 })
    end
    local ok = pcall(write_one, data.collection, data.key, { v = data.value or "" }, data.version or "")
    return respond({ ok = ok })
end

local function health(ctx, payload)
    return respond({ ok = true, now = os.time() })
end

nk.register_rpc(find_match, "find_match")
nk.register_rpc(poll_match, "poll_match")
nk.register_rpc(verify_ticket, "verify_ticket")
nk.register_rpc(reset_match, "reset_match")
nk.register_rpc(list_rooms, "list_rooms")
nk.register_rpc(kv_get, "kv_get")
nk.register_rpc(kv_put, "kv_put")
nk.register_rpc(health, "health")

nk.logger_info("fomoxa match module loaded (auto pool supported)")
