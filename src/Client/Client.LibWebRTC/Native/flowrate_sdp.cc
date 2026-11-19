#include "flowrate_sdp.h"

#include <vector>

namespace flowrate {
namespace {

// Gives the SSRCs of the lines a=ssrc and a=ssrc-group of a section the names of renames
std::string RenameSsrcs(const std::string& section, const std::map<std::string, std::string>& renames) {
  std::string result;
  size_t start = 0;

  while (start <= section.size()) {
    size_t end = section.find("\r\n", start);
    std::string line = section.substr(start, end == std::string::npos ? std::string::npos : end - start);

    if (line.rfind("a=ssrc:", 0) == 0 || line.rfind("a=ssrc-group:", 0) == 0) {
      // a=ssrc:<ssrc> <attribute>, a=ssrc-group:<semantics> <ssrc>...
      bool is_group = line[6] == '-';
      std::string renamed = line.substr(0, line.find(is_group ? ' ' : ':') + 1);
      size_t at = renamed.size();

      while (at <= line.size()) {
        size_t space = line.find(' ', at);
        std::string token = line.substr(at, space == std::string::npos ? std::string::npos : space - at);
        auto rename = renames.find(token);
        renamed += rename != renames.end() ? rename->second : token;

        // The SSRC of a=ssrc is followed by its attribute, which is kept as it is
        if (space == std::string::npos || !is_group) {
          if (space != std::string::npos)
            renamed += line.substr(space);
          break;
        }

        renamed += ' ';
        at = space + 1;
      }

      line = renamed;
    }

    result += line;

    if (end == std::string::npos)
      break;

    result += "\r\n";
    start = end + 2;
  }

  return result;
}

}  // namespace

std::string RewriteSsrcs(const std::string& sdp, const std::map<std::string, SendSsrcs>& sends) {
  std::vector<std::string> sections;
  size_t start = 0;

  for (size_t next; (next = sdp.find("\r\nm=", start + 1)) != std::string::npos; start = next)
    sections.push_back(sdp.substr(start, next - start));

  sections.push_back(sdp.substr(start));
  std::string result;

  for (auto& section : sections) {
    for (const auto& [mid, ssrcs] : sends) {
      if (section.find("\r\na=mid:" + mid + "\r\n") == std::string::npos)
        continue;

      std::map<std::string, std::string> renames;
      const std::string group = "\r\na=ssrc-group:FID ";
      const std::string marker = "\r\na=ssrc:";

      if (size_t group_at = section.find(group); group_at != std::string::npos) {
        size_t from = group_at + group.size();
        size_t space = section.find(' ', from);
        size_t end = section.find("\r\n", from);

        if (space != std::string::npos && space < end) {
          renames[section.substr(from, space - from)] = std::to_string(ssrcs.ssrc);

          if (ssrcs.rtx != 0)
            renames[section.substr(space + 1, end - space - 1)] = std::to_string(ssrcs.rtx);
        }
      } else if (size_t ssrc_at = section.find(marker); ssrc_at != std::string::npos) {
        size_t from = ssrc_at + marker.size();
        renames[section.substr(from, section.find(' ', from) - from)] = std::to_string(ssrcs.ssrc);
      }

      // A later round finds the SSRCs of the SFU there already
      section = RenameSsrcs(section, renames);
      break;
    }

    result += section;
  }

  return result;
}

}  // namespace flowrate
