<?php
if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') { http_response_code(405); exit; }
if (($_SERVER['HTTP_AUTHORIZATION'] ?? '') !== 'Bearer <YOUR_TOKEN_HERE>') { http_response_code(403); exit; }
file_put_contents('/var/log/transcriptions.log', file_get_contents('php://input') . "\n", FILE_APPEND | LOCK_EX);
http_response_code(204);
