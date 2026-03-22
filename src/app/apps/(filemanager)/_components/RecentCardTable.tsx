import React from "react";
import {
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownToggle,
  Table,
} from "reactstrap";
import Link from "next/link";
import { IconDotsVertical, IconEdit, IconTrash } from "@tabler/icons-react";
import { RecentFilesData } from "@/Data/Apps/Filemanager/Filemanager";

const RecentCardTable = () => {
  const [dropdownOpen, setDropdownOpen] = React.useState<number | null>(null);

  const toggleDropdown = (id: number) => {
    setDropdownOpen(dropdownOpen === id ? null : id);
  };
  return (
    <>
      <div className="table-responsive">
        <Table
          id="recentdatatable"
          className="table table-bottom-border recent-table align-middle table-hover mb-0"
        >
          <thead>
            <tr>
              <th>Name</th>
              <th>Total Items</th>
              <th>Size</th>
              <th>Last Modified</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {RecentFilesData.map((item) => (
              <tr key={item.id}>
                <td>
                  <div>
                    <img
                      src={item.icon}
                      className="w-20 h-20"
                      alt="file-icon"
                    />
                    <span className="ms-2 table-text">{item.name}</span>
                  </div>
                </td>
                <td className="text-success f-w-500">{item.totalItems}</td>
                <td>{item.size}</td>
                <td className="text-danger f-w-500">{item.lastModified}</td>
                <td className="d-flex">
                  <Dropdown
                    isOpen={dropdownOpen === item.id}
                    toggle={() => toggleDropdown(item.id)}
                  >
                    <DropdownToggle tag="span" className="cursor-pointer">
                      <IconDotsVertical size={18} />
                    </DropdownToggle>
                    <DropdownMenu>
                      <DropdownItem tag={Link} href="/apps/file-manager">
                        <IconEdit size={18} className="text-success me-2" />
                        Edit
                      </DropdownItem>
                      <DropdownItem tag={Link} href="/apps/file-manager">
                        <IconTrash size={18} className="text-danger me-2" />
                        Delete
                      </DropdownItem>
                    </DropdownMenu>
                  </Dropdown>
                </td>
              </tr>
            ))}
          </tbody>
        </Table>
      </div>
    </>
  );
};

export default RecentCardTable;
