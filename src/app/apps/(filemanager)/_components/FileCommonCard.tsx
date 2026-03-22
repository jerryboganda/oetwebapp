import React, { useState } from "react";
import {
  Card,
  CardBody,
  Dropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
} from "reactstrap";
import { Star } from "phosphor-react";
import { IconDotsVertical, IconEdit, IconTrash } from "@tabler/icons-react";

interface FileCardProps {
  title: string;
  image: string;
  starred?: boolean;
  onStar?: (starred: boolean) => void;
  onRename?: () => void;
  onDelete?: () => void;
}

const FileCommonCard: React.FC<FileCardProps> = ({
  title,
  image,
  starred = false,
  onStar,
  onRename,
  onDelete,
}) => {
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const toggleDropdown = () => setDropdownOpen((prev) => !prev);

  const showDropdown = onRename || onDelete;

  // const [starred, setStarred] = useState(false);
  const toggleStar = () => {
    if (onStar) {
      onStar(!starred);
    }
  };

  return (
    <Card className="quick-access border-0 position-relative">
      <CardBody className="p-3">
        <div
          className="position-absolute top-0 start-0 m-2"
          onClick={toggleStar}
        >
          <Star
            size={18}
            weight={starred ? "fill" : "regular"}
            className="text-warning"
          />
        </div>

        {showDropdown && (
          <div className="position-absolute top-0 end-0 m-2">
            <Dropdown isOpen={dropdownOpen} toggle={toggleDropdown}>
              <DropdownToggle
                tag="a"
                role="button"
                className="btn btn-link p-0 text-dark"
              >
                <IconDotsVertical size={18} />
              </DropdownToggle>
              <DropdownMenu end>
                {onRename && (
                  <DropdownItem onClick={onRename}>
                    <IconEdit size={16} className="text-success me-2" />
                    Rename
                  </DropdownItem>
                )}
                {onDelete && (
                  <DropdownItem onClick={onDelete}>
                    <IconTrash size={16} className="text-danger me-2" />
                    Delete
                  </DropdownItem>
                )}
              </DropdownMenu>
            </Dropdown>
          </div>
        )}

        <div className="text-center my-4">
          <img src={image} alt={title} className="img-fluid w-50" />
        </div>

        <p className="text-center fw-semibold mb-0">{title}</p>
      </CardBody>
    </Card>
  );
};

export default FileCommonCard;
