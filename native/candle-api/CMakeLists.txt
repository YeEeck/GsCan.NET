cmake_minimum_required(VERSION 3.16)
project(candle_api C)

add_library(candle_api SHARED
  api/candle.c
  api/candle_ctrl_req.c
)

target_include_directories(candle_api PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/api)
target_compile_definitions(candle_api PRIVATE CANDLE_API_LIBRARY UNICODE _UNICODE)
set_target_properties(candle_api PROPERTIES PREFIX "" OUTPUT_NAME "candle_api")

target_link_libraries(candle_api PRIVATE SetupAPI winusb Ole32)

install(TARGETS candle_api RUNTIME DESTINATION bin LIBRARY DESTINATION lib)
