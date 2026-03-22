import React from "react";
import { chatContactList } from "@/Data/Apps/Chat/ChatData";
import { IconPhoneCall, IconVideo } from "@tabler/icons-react";

const ContactContent = () => {
  return (
    <>
      <div className="chat-contact tabcontent chat-contact-list">
        {chatContactList.map((contact) => (
          <div className="d-flex align-items-center py-3" key={contact.id}>
            <div>
              <span
                className={`h-40 w-40 d-flex-center b-r-50 position-relative ${contact.bgColor}`}
              >
                <img
                  src={contact.avatar}
                  alt={contact.name}
                  className="img-fluid b-r-50"
                />
                <span
                  className={`position-absolute top-0 end-0 p-1 ${contact.onlineStatus} border border-light rounded-circle`}
                ></span>
              </span>
            </div>
            <div className="flex-grow-1 ps-2">
              <p className="contact-name text-dark mb-0 f-w-500">
                {contact.name}
              </p>
              <p className="mb-0 text-secondary f-s-13">{contact.phone}</p>
            </div>
            <div>
              <button className="h-35 w-35 d-flex-center b-r-50 btn-outline-success">
                <IconPhoneCall size={15} />
              </button>
            </div>
            <div>
              <button className="h-35 w-35 d-flex-center b-r-50 btn-outline-primary ms-1">
                <IconVideo size={15} />
              </button>
            </div>
          </div>
        ))}
      </div>
    </>
  );
};

export default ContactContent;
