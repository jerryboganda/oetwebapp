import React from "react";
import { ChatUpdates } from "@/Data/Apps/Chat/ChatData";

const UpdatesContent = () => {
  return (
    <>
      <div className="chat-contact tabcontent">
        {ChatUpdates.map((update) => (
          <div
            className="updates-box d-flex align-items-center py-3"
            key={update.id}
          >
            <div className={`b-2-${update.borderColor} b-r-50 p-1`}>
              <span
                className={`h-40 w-40 d-flex-center b-r-50 position-relative ${update.bgColor}`}
              >
                <img
                  src={update.avatar}
                  alt={update.name}
                  className="img-fluid b-r-50"
                />
              </span>
            </div>
            <div className="flex-grow-1 text-start ps-2">
              <span>{update.name}</span>
              <p className="f-s-12 text-secondary mb-0">{update.time}</p>
            </div>
          </div>
        ))}
      </div>
    </>
  );
};

export default UpdatesContent;
